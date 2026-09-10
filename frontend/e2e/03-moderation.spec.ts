import { testIds } from '@/testIds'

import { createListing, openListing, openQueueRow, publishApprovedListing } from './journeys'
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

/**
 * Flows 3 and 4 — a moderator approves a listing and it becomes public; a moderator rejects one
 * with a reason and the seller sees it.
 *
 * The rejection half is here because that exact path was the Phase 7 blocker: it passed every
 * in-memory test and failed against PostgreSQL, because the audit row it writes could not be
 * stored. Nothing but a real request through a real database catches that.
 */
test.describe('Moderation', () => {
  // Two registrations, a full listing with image processing, and two sign-ins. The default budget
  // is for a single journey; these are three stitched together.
  test.describe.configure({ timeout: 240_000 })

  test('an approved listing becomes publicly searchable', async ({ page }) => {
    const sellerPhone = uniquePhone('50')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Tesdiq')

    await register(page, sellerPhone, 'Təsdiq Satıcısı')
    await createListing(page, title, sellerPhone)

    // A moderator account, promoted the way a real deployment promotes the first one.
    await page.context().clearCookies()
    await register(page, moderatorPhone, 'Moderator')
    await grantRole(moderatorPhone, 'Moderator')

    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)

    await goto(page, '/admin/moderation')

    // Found by title rather than by position: the queue holds whatever earlier runs left behind.
    const row = await openQueueRow(page, title)
    await page.getByRole('button', { name: 'Təsdiqlə' }).click()

    // The row leaving the pending queue is the decision landing. Waiting on that rather than on a
    // timer is what makes the search below meaningful instead of a race.
    await expect(row).toHaveCount(0, { timeout: 30_000 })

    // Now public: the same search that found nothing before the decision finds it after.
    await page.context().clearCookies()
    await goto(page, `/axtaris?q=${encodeURIComponent(title)}`)

    await expect(
      page.getByTestId(testIds.listingCard).filter({ hasText: title }),
    ).toBeVisible({ timeout: 20_000 })
  })

  test('a rejection records its reason and the seller can read it', async ({ page }) => {
    const sellerPhone = uniquePhone('50')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Redd')
    const reason = 'Şəkillər məhsulu aydın göstərmir.'

    await register(page, sellerPhone, 'Rədd Satıcısı')
    await createListing(page, title, sellerPhone)

    await page.context().clearCookies()
    await register(page, moderatorPhone, 'Rədd Moderatoru')
    await grantRole(moderatorPhone, 'Moderator')

    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)

    await goto(page, '/admin/moderation')
    await openQueueRow(page, title)
    await page.getByRole('button', { name: 'Rədd et' }).click()

    const dialog = page.getByRole('dialog', { name: 'Elanı rədd et' })
    await dialog.getByLabel('Səbəb *').fill(reason)
    await dialog.getByRole('button', { name: 'Rədd et' }).click()

    // The decision persisted — this is the write that failed against PostgreSQL before Phase 7.
    await expect(page.getByRole('dialog')).toHaveCount(0, { timeout: 20_000 })

    await page.context().clearCookies()
    await login(page, sellerPhone)
    await goto(page, '/kabinet/elanlarim?status=rejected')

    await expect(page.getByText(reason)).toBeVisible({ timeout: 20_000 })

    // The rejection also left an in-app notification — the badge, the reason inside it, and reading
    // it clearing the badge are the whole point of the notification existing at all.
    await expect(page.getByRole('link', { name: /Bildirişlər, 1 oxunmamış/ })).toBeVisible({
      timeout: 20_000,
    })

    await page.getByRole('link', { name: /Bildirişlər/ }).click()
    const card = page.getByRole('button', { name: /Elanınız rədd edildi/ })
    await expect(card).toBeVisible()
    await expect(card.getByText(reason)).toBeVisible()

    await card.click()
    await expect(page.getByRole('link', { name: 'Bildirişlər' })).toBeVisible({ timeout: 20_000 })
  })

  test('a moderator blocks an already-live listing, and it disappears from the public site', async ({
    page,
  }) => {
    const sellerPhone = uniquePhone('50')
    const adminPhone = uniquePhone('60')
    const title = uniqueTitle('Blok')
    const reason = 'Qaydalara zidd məzmun.'

    // Auditing the decision needs Admin (the audit trail is Admin-only, PD-7.4), so the same
    // account both blocks the listing and reads the record afterwards.
    await publishApprovedListing(page, {
      title,
      sellerPhone,
      operatorPhone: adminPhone,
      sellerName: 'Blok Satıcısı',
      operatorName: 'Blok Admini',
      operatorRole: 'Admin',
    })

    // The listing's own address, captured while it is still live and public — this is what proves
    // "gone" means gone from the page itself, not only absent from a search result.
    await openListing(page, title)
    const detailUrl = page.url()

    await goto(page, '/admin/moderation')
    await page.getByLabel('Növbə').selectOption('active')

    const row = page.getByTestId(testIds.queueRow).filter({ hasText: title })
    await expect(row).toBeVisible({ timeout: 30_000 })

    await row.getByRole('button', { name: 'Aç' }).click()
    await expect(page.getByRole('heading', { name: title })).toBeVisible()

    // A listing that is already live is not waiting for a first decision — approve/reject are the
    // pending queue's actions, not this one's.
    await expect(page.getByRole('button', { name: 'Təsdiqlə' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Rədd et' })).toHaveCount(0)

    await page.getByRole('button', { name: 'Blokla' }).click()

    const dialog = page.getByRole('dialog', { name: 'Elanı blokla' })
    await dialog.getByLabel('Səbəb *').fill(reason)
    await dialog.getByRole('button', { name: 'Blokla' }).click()

    // Leaving the active queue is the decision landing.
    await expect(row).toHaveCount(0, { timeout: 30_000 })

    // Gone from public search, and gone from its own page — the same public Active-only rule a
    // rejection or an expiry already enforces. Checked through the category page rather than the
    // exact "/axtaris" query used above: search responses are cacheable for a minute (deliberately —
    // that architecture is unrelated to this test), and re-issuing the identical request would read
    // back the pre-block cache entry instead of proving anything about the server's current state.
    await page.context().clearCookies()
    await goto(page, `/elanlar/ov-cantalari?q=${encodeURIComponent(title)}`)
    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)

    await goto(page, detailUrl)
    await expect(
      page.getByText('Bu nömrəli elan mövcud deyil və ya ləğv olunub.'),
    ).toBeVisible({ timeout: 20_000 })

    // The seller can still see it — in its own bucket, with the reason — which is the whole point
    // of Blocked staying a distinct state instead of vanishing along with the public listing.
    await login(page, sellerPhone)
    await goto(page, '/kabinet/elanlarim?status=blocked')
    await expect(page.getByText(reason)).toBeVisible({ timeout: 20_000 })

    // And the decision is on the append-only record an Admin can read back.
    await page.context().clearCookies()
    await login(page, adminPhone)
    await goto(page, '/admin/audit')
    await expect(page.getByText(reason)).toBeVisible({ timeout: 30_000 })
  })
})
