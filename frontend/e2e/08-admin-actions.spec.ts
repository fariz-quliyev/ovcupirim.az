import { testIds } from '@/testIds'

import {
  expect,
  goto,
  grantRole,
  login,
  register,
  signInAsOperator,
  test,
  uniquePhone,
  uniqueTitle,
} from './fixtures'
import { createListing, openQueueRow } from './journeys'

/**
 * Flow 9 — the operator actions that change what the public can see, and who may take them.
 *
 * Blocking and role management are the two places where the panel is not merely reading: one takes
 * a listing off the site, the other decides who is allowed to. Both are audited, and the audit
 * entry is part of the behaviour rather than a side effect — it is the only record of why.
 */
test.describe('Admin actions', () => {
  test.describe.configure({ timeout: 240_000 })

  test('blocking a listing takes it off the site and records why', async ({ page }) => {
    const sellerPhone = uniquePhone('55')
    const adminPhone = uniquePhone('70')
    const title = uniqueTitle('Bloklanan')
    const reason = `E2E yoxlaması: ${title} qaydalara uyğun deyil.`

    await register(page, sellerPhone, 'Bloklanan Satıcı')
    await createListing(page, title, sellerPhone)

    await page.context().clearCookies()
    await register(page, adminPhone, 'Bloklayan Admin')
    await grantRole(adminPhone, 'Admin')
    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)

    await goto(page, '/admin/moderation')
    const row = await openQueueRow(page, title)

    await page.getByRole('button', { name: 'Blokla' }).click()

    const dialog = page.getByRole('dialog', { name: 'Elanı blokla' })
    await dialog.getByLabel('Səbəb *').fill(reason)
    await dialog.getByRole('button', { name: 'Blokla' }).click()

    // The row leaving the queue is the decision landing, not a spinner finishing.
    await expect(row).toHaveCount(0, { timeout: 30_000 })

    // Append-only and Admin-only: the reason the operator typed is what the record has to carry,
    // and this is the one place it can be read back.
    await goto(page, '/admin/audit')
    await expect(page.getByText(reason)).toBeVisible({ timeout: 30_000 })

    await page.context().clearCookies()
    await goto(page, `/axtaris?q=${encodeURIComponent(title)}`)

    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)
  })

  test('an administrator grants and revokes the moderator role', async ({ page }) => {
    const adminPhone = uniquePhone('70')
    const targetPhone = uniquePhone('77')

    await register(page, targetPhone, 'Namizəd İstifadəçi')

    await page.context().clearCookies()
    await register(page, adminPhone, 'Rol Admini')
    await grantRole(adminPhone, 'Admin')
    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)

    // Users are inspect-only apart from this one mutation (PD-7.2).
    await goto(page, `/admin/users?q=${encodeURIComponent(targetPhone)}`)

    const row = page.getByTestId(testIds.queueRow).filter({ hasText: targetPhone })
    await expect(row).toBeVisible({ timeout: 30_000 })

    await row.getByRole('button', { name: 'Moderator et' }).click()
    await page.getByRole('dialog', { name: 'Moderator et' }).getByRole('button', { name: 'Moderator et' }).click()

    await expect(row.getByRole('button', { name: 'Moderatorluğu götür' })).toBeVisible({
      timeout: 30_000,
    })

    // The grant is only real if it reaches the guard, and the guard reads a freshly minted token.
    await page.context().clearCookies()
    await signInAsOperator(page, targetPhone)

    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)
    await goto(page, `/admin/users?q=${encodeURIComponent(targetPhone)}`)

    const granted = page.getByTestId(testIds.queueRow).filter({ hasText: targetPhone })
    await granted.getByRole('button', { name: 'Moderatorluğu götür' }).click()
    await page
      .getByRole('dialog', { name: 'Moderatorluğu götür' })
      .getByRole('button', { name: 'Götür' })
      .click()

    await expect(granted.getByRole('button', { name: 'Moderator et' })).toBeVisible({
      timeout: 30_000,
    })

    await page.context().clearCookies()
    await login(page, targetPhone)
    await goto(page, '/admin')

    // Revoked means turned away, not merely a hidden menu item.
    await expect(page).toHaveURL(/localhost:5173\/$/, { timeout: 20_000 })
  })
})
