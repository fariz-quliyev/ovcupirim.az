import { testIds } from '@/testIds'

import { expect, goto, grantRole, register, signInAsOperator, test, uniquePhone, uniqueTitle } from './fixtures'
import { approveListing, createListing } from './journeys'

/**
 * Flow 5 — the buyer journey: search, then narrow with filters.
 *
 * Exercises the hand-written catalogue SQL through a browser: the folded text fallback, a category
 * filter, an attribute filter and the URL-as-state behaviour that makes a result set shareable.
 */
test.describe('Search and filters', () => {
  test.describe.configure({ timeout: 240_000 })

  test('an approved listing is findable, filterable and shareable', async ({ page }) => {
    const sellerPhone = uniquePhone('50')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Axtaris')

    await register(page, sellerPhone, 'Axtarış Satıcısı')
    await createListing(page, title, sellerPhone)

    await page.context().clearCookies()
    await register(page, moderatorPhone, 'Axtarış Moderatoru')
    await grantRole(moderatorPhone, 'Moderator')
    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)
    await approveListing(page, title)

    await page.context().clearCookies()

    // Found by text.
    await goto(page, `/axtaris?q=${encodeURIComponent(title)}`)
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toBeVisible({
      timeout: 20_000,
    })

    // Narrowed by category: the listing sits in "Ov çantaları", so its own category keeps it and
    // a sibling category does not.
    await goto(page, `/elanlar/ov-cantalari?q=${encodeURIComponent(title)}`)
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toBeVisible()

    await goto(page, `/elanlar/tilovlar?q=${encodeURIComponent(title)}`)
    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)
  })

  test('a price filter excludes what falls outside it, and the URL carries the query', async ({
    page,
  }) => {
    const sellerPhone = uniquePhone('50')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Qiymet')

    await register(page, sellerPhone, 'Qiymət Satıcısı')
    await createListing(page, title, sellerPhone)

    await page.context().clearCookies()
    await register(page, moderatorPhone, 'Qiymət Moderatoru')
    await grantRole(moderatorPhone, 'Moderator')
    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)
    await approveListing(page, title)

    await page.context().clearCookies()

    // The listing is priced at 120.
    await goto(page, `/axtaris?q=${encodeURIComponent(title)}&priceMin=200`)
    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)

    await goto(page, `/axtaris?q=${encodeURIComponent(title)}&priceMax=200`)
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toBeVisible()

    // A filter chosen in the UI lands in the URL, which is what makes a result set shareable and
    // survive the back button.
    await page.getByLabel('Sıralama').selectOption('price_asc')
    await expect(page).toHaveURL(/sort=price_asc/)
  })

  test('an attribute filter narrows by the category schema', async ({ page }) => {
    const sellerPhone = uniquePhone('50')
    const moderatorPhone = uniquePhone('51')
    const title = uniqueTitle('Hecm')

    await register(page, sellerPhone, 'Həcm Satıcısı')
    // createListing sets the volume attribute to 25.
    await createListing(page, title, sellerPhone)

    await page.context().clearCookies()
    await register(page, moderatorPhone, 'Həcm Moderatoru')
    await grantRole(moderatorPhone, 'Moderator')
    await page.context().clearCookies()
    await signInAsOperator(page, moderatorPhone)
    await approveListing(page, title)

    await page.context().clearCookies()

    // Attribute filters only exist inside a category, which is where the schema is known.
    const base = `/elanlar/ov-cantalari?q=${encodeURIComponent(title)}`

    await goto(page, `${base}&attr.volume_min=10&attr.volume_max=40`)
    await expect(page.getByTestId(testIds.listingCard).filter({ hasText: title })).toBeVisible()

    await goto(page, `${base}&attr.volume_min=50`)
    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)
  })
})
