import type { Locator, Page } from '@playwright/test'

import { testIds } from '@/testIds'

import {
  expect,
  fillField,
  goto,
  grantRole,
  register,
  sampleImagePath,
  signInAsOperator,
} from './fixtures'

/** The create-and-submit path, for tests whose subject is what happens afterwards. */
export async function createListing(page: Page, title: string, phone: string): Promise<void> {
  await goto(page, '/yeni-elan')

  // The taxonomy arrives after the page does; waiting for the picker itself is the difference
  // between a stable test and one that races the first fetch.
  await expect(page.getByTestId(testIds.categoryPicker)).toBeVisible({ timeout: 30_000 })

  await page.getByRole('button', { name: 'Ovçuluq' }).click()
  await page.getByRole('button', { name: 'Ov çantaları' }).click()

  await fillField(page, 'Başlıq *', title)
  await fillField(page, 'Təsvir *', 'E2E yoxlaması üçün yaradılmış elan.')
  await fillField(page, 'Qiymət, ₼', '120')
  await fillField(page, 'Həcm, L *', '25')
  await page.getByLabel('Şəhər / rayon').selectOption({ index: 1 })
  await fillField(page, 'Əlaqə nömrəsi *', phone)

  await page.getByRole('button', { name: 'Qaralamanı yadda saxla' }).click()
  await expect(page.getByText(/Qaralama yadda saxlanıldı/)).toBeVisible({ timeout: 15_000 })

  await page.getByLabel('Şəkil seçin').setInputFiles(await sampleImagePath(page))
  await expect(page.getByText('Əsas şəkil')).toBeVisible({ timeout: 30_000 })

  await page.getByRole('button', { name: 'Elanı yerləşdir' }).click()

  // Asserted on the listing itself, not on the word "Gözləmədə": that is also a tab label, so a
  // failed publish would otherwise look like a successful one.
  await page.waitForURL(/\/kabinet\/elanlarim/, { timeout: 20_000 })
  await expect(page.getByText(title).first()).toBeVisible({ timeout: 20_000 })
}

/**
 * Opens one queue row by title.
 *
 * The queue is fetched after the admin shell renders, so waiting for the page is not the same as
 * waiting for the queue: Playwright's retrying does the waiting here, against a stable hook on the
 * shared table.
 */
export async function openQueueRow(page: Page, title: string): Promise<Locator> {
  const row = page.getByTestId(testIds.queueRow).filter({ hasText: title })

  await expect(row).toBeVisible({ timeout: 30_000 })
  await row.getByRole('button', { name: 'Aç' }).click()
  await expect(page.getByRole('heading', { name: title })).toBeVisible()

  return row
}

/**
 * Approves one pending listing and waits for the decision to land.
 *
 * The row leaving the queue is the signal — waiting on that rather than on a timer is what makes a
 * subsequent public read meaningful instead of a race against the mutation.
 */
export async function approveListing(page: Page, title: string): Promise<void> {
  await goto(page, '/admin/moderation')

  const row = await openQueueRow(page, title)
  await page.getByRole('button', { name: 'Təsdiqlə' }).click()

  await expect(row).toHaveCount(0, { timeout: 30_000 })
}

/**
 * A published, approved listing and an operator signed in to act on it.
 *
 * Five of the eight journeys need a listing that is actually live, which takes two accounts and a
 * moderation decision to arrange. Repeating that inline made the tests longer than the behaviour
 * they were checking, and the setup is not what any of them is asserting.
 *
 * Leaves the browser signed in as the operator, whose role is the caller's to choose: moderation
 * and reports are open to a Moderator, while stores, users and the audit trail are Admin only.
 */
export async function publishApprovedListing(
  page: Page,
  options: {
    title: string
    sellerPhone: string
    operatorPhone: string
    sellerName: string
    operatorName: string
    operatorRole?: 'Moderator' | 'Admin'
  },
): Promise<void> {
  await register(page, options.sellerPhone, options.sellerName)
  await createListing(page, options.title, options.sellerPhone)

  await page.context().clearCookies()
  await register(page, options.operatorPhone, options.operatorName)
  await grantRole(options.operatorPhone, options.operatorRole ?? 'Moderator')

  await page.context().clearCookies()
  await signInAsOperator(page, options.operatorPhone)
  await approveListing(page, options.title)
}

/** Opens a public listing the way a buyer reaches it: by finding it. */
export async function openListing(page: Page, title: string): Promise<void> {
  await goto(page, `/axtaris?q=${encodeURIComponent(title)}`)

  const card = page.getByTestId(testIds.listingCard).filter({ hasText: title })
  await expect(card).toBeVisible({ timeout: 20_000 })
  await card.getByRole('link').first().click()

  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeVisible({ timeout: 20_000 })
}
