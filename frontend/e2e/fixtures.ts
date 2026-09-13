import { expect, test as base } from '@playwright/test'
import type { APIRequestContext, Page } from '@playwright/test'

import { MARKER } from './global-setup'

const API = 'http://localhost:5080/api/v1'

/**
 * A phone number no other run will reuse.
 *
 * The database is shared and never reset between runs, so every account, listing and store these
 * tests create has to be unique. Azerbaijani mobile prefixes are fixed, so the entropy goes in the
 * last seven digits.
 */
export function uniquePhone(prefix = '55'): string {
  const digits = String(Date.now() % 10_000_000).padStart(7, '0')

  return `+994${prefix}${digits}`
}

/**
 * A single-token title.
 *
 * No spaces: the queue renders a title and its metadata as adjacent elements, so a multi-word
 * title is matched against text that has been run together, and a substring filter stops being
 * reliable. One token sidesteps that entirely.
 */
export function uniqueTitle(what: string): string {
  return `${what}-${Date.now().toString(36)}`
}

/**
 * Completes a real registration, including the one-time code.
 *
 * The code comes from a Development-only probe endpoint that reads back what the SMS sender
 * captured. Outside Development that route does not exist and the probe is not registered, so this
 * cannot become a way into a deployed environment.
 */
/**
 * Navigates and waits for the app to be interactive.
 *
 * Filling a field before React has attached its listeners sets the DOM value and nothing else —
 * the component's state stays empty and the submit button stays disabled. That race only shows up
 * against a cold dev server, which is exactly the first test of a CI run.
 */
export async function goto(page: Page, path: string): Promise<void> {
  await page.goto(path)

  // Rendered by React on every page, so its presence means hydration is done.
  await page.getByRole('link', { name: /OVCUPIRIM/i }).first().waitFor({ state: 'visible' })
}

/**
 * Fills a field and checks it took.
 *
 * A controlled React input can be filled in the window between the element existing and its
 * handler being attached: the DOM shows the text, the component's state stays empty, and the
 * submit button stays disabled for reasons nothing on screen explains.
 */
export async function fillField(page: Page, label: string, value: string): Promise<void> {
  const field = page.getByLabel(label)

  for (let attempt = 0; attempt < 5; attempt++) {
    await field.fill(value)

    if ((await field.inputValue()) === value) {
      return
    }

    await page.waitForTimeout(200)
  }

  throw new Error(`"${label}" would not accept a value.`)
}

export async function register(page: Page, phone: string, fullName = 'İstifadəçi'): Promise<void> {
  await goto(page, '/qeydiyyat')

  // Every account these tests create is marked, so global setup can remove exactly them and
  // nothing else.
  await fillField(page, 'Ad və soyad', `${fullName} ${MARKER}`)
  await fillField(page, 'Mobil nömrə', phone)

  const submit = page.getByRole('button', { name: 'Davam et' })
  await expect(submit).toBeEnabled()
  await submit.click()

  await fillCode(page, await otpFor(page.request, phone))

  // Registration lands on the account area; waiting for the URL is the signal that the session
  // was actually established rather than that a spinner rendered.
  await page.waitForURL((url) => !url.pathname.startsWith('/qeydiyyat'), { timeout: 15_000 })
}

/** Signs an existing account in. */
export async function login(page: Page, phone: string): Promise<void> {
  await goto(page, '/giris')

  await fillField(page, 'Mobil nömrə', phone)

  const submit = page.getByRole('button', { name: 'Kodu göndər' })
  await expect(submit).toBeEnabled()
  await submit.click()

  // A code was just sent for the registration, so the resend cooldown may still be running. It is
  // one second in Development; wait it out and ask again rather than pretending it is not there.
  const codeField = page.getByLabel('Təsdiq kodu')

  try {
    await codeField.waitFor({ state: 'visible', timeout: 3_000 })
  } catch {
    await page.waitForTimeout(1_500)
    await submit.click()
    await codeField.waitFor({ state: 'visible', timeout: 10_000 })
  }

  await fillCode(page, await otpFor(page.request, phone))

  // The click that submits the code returns before the session exists; waiting for the redirect is
  // what makes "signed in" true rather than merely requested.
  await page.waitForURL((url) => !url.pathname.startsWith('/giris'), { timeout: 15_000 })
}

async function fillCode(page: Page, code: string): Promise<void> {
  await fillField(page, 'Təsdiq kodu', code)
  await page.getByRole('button', { name: 'Təsdiqlə' }).click()
}

export async function otpFor(request: APIRequestContext, phone: string): Promise<string> {
  // The sender records it synchronously, but the request that triggered it may still be settling.
  for (let attempt = 0; attempt < 20; attempt++) {
    const response = await request.get(`${API}/dev/otp/${encodeURIComponent(phone)}`)

    if (response.ok()) {
      const body = (await response.json()) as { code: string }

      if (body.code) {
        return body.code
      }
    }

    await new Promise((resolve) => setTimeout(resolve, 250))
  }

  throw new Error(`No OTP was captured for ${phone}. Is the API running in Development?`)
}

/**
 * Signs in and confirms the operator surface is actually reachable.
 *
 * A role change only reaches the browser through a freshly minted token, so signing in before the
 * grant lands leaves the guard bouncing the visitor to the public site. Asserting the admin shell
 * here turns that into a clear failure instead of a missing table row three steps later.
 */
export async function signInAsOperator(page: Page, phone: string): Promise<void> {
  await login(page, phone)
  await goto(page, '/admin')

  await expect(page.getByRole('navigation', { name: 'İdarəetmə bölmələri' })).toBeVisible({
    timeout: 15_000,
  })
}

/**
 * Promotes an account to Moderator or Admin.
 *
 * There is deliberately no API that grants Admin, so the first one is made the way a real
 * deployment makes it: directly in the database. This runs `psql` in the development container.
 */
export async function grantRole(phone: string, role: 'Moderator' | 'Admin'): Promise<void> {
  const { execFile } = await import('node:child_process')
  const { promisify } = await import('node:util')
  const run = promisify(execFile)

  const value = role === 'Admin' ? 2 : 1

  await run('docker', [
    'exec',
    'ovcuprim-postgres',
    'psql',
    '-U',
    'ovcuprim',
    '-d',
    'ovcuprim',
    '-c',
    `UPDATE "Users" SET "Role" = ${value} WHERE "PhoneNumber" = '${phone}'`,
  ])
}

/**
 * A real JPEG of a size the upload pipeline accepts, written to disk once per run.
 *
 * The processor requires at least 200×200 and decodes what it is given, so a token 1×1 file is
 * rejected as out of bounds — correctly. The image is drawn in the browser rather than embedded as
 * a base64 blob, which keeps the fixture honest and readable.
 */
export async function sampleImagePath(page?: Page): Promise<string> {
  const { writeFile, mkdir, access } = await import('node:fs/promises')
  const { join } = await import('node:path')
  const { tmpdir } = await import('node:os')

  const directory = join(tmpdir(), 'ovcupirim-e2e')
  await mkdir(directory, { recursive: true })

  const path = join(directory, 'listing.jpg')

  if (await access(path).then(() => true).catch(() => false)) {
    return path
  }

  if (!page) {
    throw new Error('The first call needs a page to draw the image with.')
  }

  const dataUrl = await page.evaluate(() => {
    const canvas = document.createElement('canvas')
    canvas.width = 800
    canvas.height = 600

    const context = canvas.getContext('2d')!

    // Some actual content: a flat fill can compress to almost nothing, and a realistic file is a
    // better exercise of the decode-and-re-encode path.
    context.fillStyle = '#2f4f2f'
    context.fillRect(0, 0, 800, 600)

    for (let i = 0; i < 400; i++) {
      context.fillStyle = `hsl(${(i * 7) % 360} 60% ${30 + (i % 40)}%)`
      context.fillRect((i * 37) % 800, (i * 53) % 600, 24, 18)
    }

    return canvas.toDataURL('image/jpeg', 0.8)
  })

  await writeFile(path, Buffer.from(dataUrl.split(',')[1]!, 'base64'))

  return path
}

export const test = base
export { expect }
