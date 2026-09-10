import { testIds } from '@/testIds'

import { expect, goto, register, test, uniquePhone, uniqueTitle } from './fixtures'
import { openListing, publishApprovedListing } from './journeys'

/**
 * Flow 7 — a buyer saves a listing and finds it again.
 *
 * Favouriting is one of the few writes an ordinary buyer makes, and the round trip is what matters:
 * the button reflecting the new state is not the same as the listing actually being on the saved
 * list, which is read back through a different endpoint on a different page.
 */
test.describe('Favourites', () => {
  test.describe.configure({ timeout: 240_000 })

  test('a buyer saves a listing, finds it on the saved list and removes it', async ({ page }) => {
    const sellerPhone = uniquePhone('55')
    const moderatorPhone = uniquePhone('51')
    const buyerPhone = uniquePhone('77')
    const title = uniqueTitle('Secilmis')

    await publishApprovedListing(page, {
      title,
      sellerPhone,
      operatorPhone: moderatorPhone,
      sellerName: 'Seçilmiş Satıcı',
      operatorName: 'Seçilmiş Moderator',
    })

    await page.context().clearCookies()
    await register(page, buyerPhone, 'Seçilmiş Alıcı')

    await openListing(page, title)
    await page.getByRole('button', { name: 'Seçilmişlərə əlavə et' }).click()

    // The button carries the state, so it flipping is the listing having been saved.
    await expect(page.getByRole('button', { name: 'Seçilmişlərdən çıxar' })).toBeVisible({
      timeout: 20_000,
    })

    await goto(page, '/secilmisler')
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toBeVisible({
      timeout: 20_000,
    })

    // And removing it takes it back off the list, rather than only off the screen it was clicked on.
    await openListing(page, title)
    await page.getByRole('button', { name: 'Seçilmişlərdən çıxar' }).click()
    await expect(page.getByRole('button', { name: 'Seçilmişlərə əlavə et' })).toBeVisible({
      timeout: 20_000,
    })

    await goto(page, '/secilmisler')
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toHaveCount(0)
  })

  test('an anonymous visitor is sent to sign in rather than silently failing', async ({ page }) => {
    const sellerPhone = uniquePhone('55')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Qonaq')

    await publishApprovedListing(page, {
      title,
      sellerPhone,
      operatorPhone: moderatorPhone,
      sellerName: 'Qonaq Satıcı',
      operatorName: 'Qonaq Moderator',
    })

    await page.context().clearCookies()
    await openListing(page, title)

    // Signed out, the control is a link to the sign-in page — a saved listing needs somewhere to
    // belong, and a button that quietly did nothing would be worse than no button.
    await page.getByRole('link', { name: 'Seçilmişlərə əlavə et' }).click()
    await expect(page).toHaveURL(/\/giris/)
  })
})
