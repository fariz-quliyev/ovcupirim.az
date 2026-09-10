import type { Page } from '@playwright/test'

import { testIds } from '@/testIds'

import {
  expect,
  fillField,
  goto,
  grantRole,
  register,
  signInAsOperator,
  test,
  uniquePhone,
  uniqueTitle,
} from './fixtures'

/**
 * Flow 6 — a seller applies for a storefront and an administrator decides on it.
 *
 * The two halves are worth driving together: an application that looks accepted to the seller but
 * never reaches the queue, and an approval that never makes the storefront public, are both
 * invisible to a test that exercises only one side of the exchange.
 */
test.describe('Store application', () => {
  test.describe.configure({ timeout: 240_000 })

  /** Submits the application. A seller without a store sees only this form on "Mağazam". */
  async function applyForStore(page: Page, name: string, phone: string): Promise<void> {
    await goto(page, '/kabinet/magazam')
    await expect(page.getByRole('heading', { name: 'Mağaza aç' })).toBeVisible({ timeout: 20_000 })

    await fillField(page, 'Mağazanın adı *', name)
    await fillField(page, 'Haqqında', 'E2E yoxlaması üçün açılan mağaza.')
    await fillField(page, 'Ünvan', 'Bakı, Nizami küçəsi 1')
    await fillField(page, 'Mağaza nömrəsi', phone)

    await page.getByRole('button', { name: 'Müraciət göndər' }).click()

    // The seller's own view flips from the application form to the store they now own, which is
    // how they learn the application was filed rather than silently dropped.
    await expect(page.getByRole('heading', { name: 'Mağazam' })).toBeVisible({ timeout: 20_000 })
  }

  /** Approves a pending application and waits for the decision to land. */
  async function approveStore(page: Page, name: string): Promise<void> {
    await goto(page, '/admin/stores')

    const pending = page.getByTestId(testIds.queueRow).filter({ hasText: name })
    await expect(pending).toBeVisible({ timeout: 30_000 })

    await pending.getByRole('button', { name: 'Təsdiqlə' }).click()
    await page.getByRole('dialog', { name }).getByRole('button', { name: 'Təsdiq et' }).click()

    // Leaving the pending queue is the signal that the decision landed.
    await expect(pending).toHaveCount(0, { timeout: 30_000 })
  }

  test('an application reaches the queue and approval makes the storefront public', async ({
    page,
  }) => {
    const sellerPhone = uniquePhone('55')
    const adminPhone = uniquePhone('70')
    const storeName = uniqueTitle('Magaza')

    await register(page, sellerPhone, 'Mağaza Satıcısı')
    await applyForStore(page, storeName, sellerPhone)

    await page.context().clearCookies()
    await register(page, adminPhone, 'Mağaza Admini')
    await grantRole(adminPhone, 'Admin')
    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)

    // Stores are an Admin-only queue, and a pending application is what it opens on.
    await approveStore(page, storeName)

    await page.getByLabel('Status').selectOption('Active')

    const active = page.getByTestId(testIds.queueRow).filter({ hasText: storeName })
    await expect(active).toBeVisible({ timeout: 30_000 })

    // Following the operator's own link proves the storefront is reachable, without this test
    // having to guess how a name becomes a URL.
    await active.getByRole('link', { name: 'Bax' }).click()
    await expect(page.getByRole('heading', { name: storeName })).toBeVisible({ timeout: 20_000 })
  })

  test('a suspended store stops being public', async ({ page }) => {
    const sellerPhone = uniquePhone('55')
    const adminPhone = uniquePhone('70')
    const storeName = uniqueTitle('Dayandirilan')

    await register(page, sellerPhone, 'Dayandırılan Satıcı')
    await applyForStore(page, storeName, sellerPhone)

    await page.context().clearCookies()
    await register(page, adminPhone, 'Dayandıran Admin')
    await grantRole(adminPhone, 'Admin')
    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)

    await approveStore(page, storeName)
    await page.getByLabel('Status').selectOption('Active')

    const active = page.getByTestId(testIds.queueRow).filter({ hasText: storeName })
    await expect(active).toBeVisible({ timeout: 30_000 })

    const storefront = await active.getByRole('link', { name: 'Bax' }).getAttribute('href')
    expect(storefront).not.toBeNull()

    // Suspension takes a reason, which the audit trail keeps.
    await active.getByRole('button', { name: 'Dayandır' }).click()

    const dialog = page.getByRole('dialog', { name: 'Mağazanı dayandır' })
    await dialog.getByLabel('Səbəb *').fill('E2E yoxlaması: mağaza müvəqqəti dayandırılır.')
    await dialog.getByRole('button', { name: 'Dayandır' }).click()

    await expect(active).toHaveCount(0, { timeout: 30_000 })

    await page.context().clearCookies()
    await goto(page, storefront!)

    // A suspended storefront is gone for the public, not merely unlisted.
    await expect(page.getByRole('heading', { name: storeName })).toHaveCount(0)
  })
})
