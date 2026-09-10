import { testIds } from '@/testIds'

import {
  expect,
  goto,
  register,
  signInAsOperator,
  test,
  uniquePhone,
  uniqueTitle,
} from './fixtures'
import { openListing, publishApprovedListing } from './journeys'

/**
 * Flow 8 — a buyer reports a listing and a moderator closes the report.
 *
 * This is the one path where a member of the public puts work into the operators' queue, so the
 * handover is the subject: a report that the reporter believes was filed but that never appears in
 * the queue is the failure this exists to catch.
 */
test.describe('Reporting', () => {
  test.describe.configure({ timeout: 240_000 })

  test('a report reaches the moderation queue and can be resolved', async ({ page }) => {
    const sellerPhone = uniquePhone('55')
    const moderatorPhone = uniquePhone('51')
    const buyerPhone = uniquePhone('77')
    const title = uniqueTitle('Sikayet')

    await publishApprovedListing(page, {
      title,
      sellerPhone,
      operatorPhone: moderatorPhone,
      sellerName: 'Şikayət Satıcı',
      operatorName: 'Şikayət Moderator',
    })

    await page.context().clearCookies()
    await register(page, buyerPhone, 'Şikayət Alıcı')

    await openListing(page, title)
    await page.getByRole('button', { name: 'Şikayət et' }).click()

    await page.getByLabel('Səbəb').selectOption('WrongCategory')
    await page.getByLabel('Şərh (istəyə bağlı)').fill('E2E yoxlaması: kateqoriya səhvdir.')
    await page.getByRole('button', { name: 'Göndər' }).click()

    // The reporter is told it was filed. Whether that is true is the next half of the test.
    await expect(page.getByText('Şikayətiniz qeydə alındı. Moderatorlar yoxlayacaq.')).toBeVisible({
      timeout: 20_000,
    })

    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)

    await goto(page, '/admin/reports')

    const row = page.getByTestId(testIds.queueRow).filter({ hasText: title })
    await expect(row).toBeVisible({ timeout: 30_000 })

    await row.getByRole('button', { name: 'Həll et' }).click()

    // Closing a report needs no reason: the decision is recorded against the report itself, and
    // the listing is untouched either way.
    await page
      .getByRole('dialog', { name: 'Şikayəti həll edilmiş kimi bağla' })
      .getByRole('button', { name: 'Həll edildi' })
      .click()

    // Resolving takes it out of the open queue, which is what stops two moderators working the
    // same report.
    await expect(row).toHaveCount(0, { timeout: 30_000 })
  })
})
