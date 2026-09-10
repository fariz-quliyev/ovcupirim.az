import { expect, fillField, goto, register, sampleImagePath, test, uniquePhone, uniqueTitle } from './fixtures'
import { testIds } from '@/testIds'

import { createListing } from './journeys'

/**
 * Flows 2 and 3 — create a listing, upload an image, submit it for moderation, and confirm it is
 * not public until a moderator says so.
 *
 * This is the core seller journey and the only place untrusted bytes enter the system, so it is
 * the flow worth having a browser for.
 */
test.describe('Listing creation', () => {
  test('a seller creates, illustrates and submits a listing', async ({ page }) => {
    const phone = uniquePhone('50')
    const title = uniqueTitle('OvCantasi')

    await register(page, phone, 'Elan Satıcısı')

    await goto(page, '/yeni-elan')

    // Two-pane picker: a top-level category, then a leaf.
    await expect(page.getByTestId(testIds.categoryPicker)).toBeVisible({ timeout: 30_000 })
    await page.getByRole('button', { name: 'Ovçuluq' }).click()
    await page.getByRole('button', { name: 'Ov çantaları' }).click()

    await fillField(page, 'Başlıq *', title)
    await fillField(page, 'Təsvir *', 'Az istifadə olunub, təmiz saxlanılıb. E2E yoxlaması.')
    await fillField(page, 'Qiymət, ₼', '150')

    // The category schema drives this field; the form never hard-codes it.
    await fillField(page, 'Həcm, L *', '30')

    await page.getByLabel('Şəhər / rayon').selectOption({ index: 1 })
    await fillField(page, 'Əlaqə nömrəsi *', phone)

    // Images need a listing to belong to, so the draft is saved first.
    await page.getByRole('button', { name: 'Qaralamanı yadda saxla' }).click()
    await expect(page.getByText(/Qaralama yadda saxlanıldı/)).toBeVisible({ timeout: 15_000 })

    await page.getByLabel('Şəkil seçin').setInputFiles(await sampleImagePath(page))
    await expect(page.getByText('Əsas şəkil')).toBeVisible({ timeout: 30_000 })

    await page.getByRole('button', { name: 'Elanı yerləşdir' }).click()

    // Everything published enters moderation; nothing goes live on the seller's word.
    await page.waitForURL(/\/kabinet\/elanlarim/, { timeout: 20_000 })
    await expect(page.getByText(title).first()).toBeVisible({ timeout: 20_000 })
  })

  test('a submitted listing is not public before a moderator approves it', async ({ page }) => {
    const phone = uniquePhone('50')
    const title = uniqueTitle('GizliCanta')

    await register(page, phone, 'Gözləyən Satıcı')
    await createListing(page, title, phone)

    // Signed out, the catalogue must not show it: everything published waits for a moderator.
    await page.context().clearCookies()
    await goto(page, `/axtaris?q=${encodeURIComponent(title)}`)

    // Scoped to result cards: the search heading echoes the query back, so a bare text match would
    // find the term the visitor just typed and prove nothing.
    await expect(page.getByTestId(testIds.listingCard)).toHaveCount(0)
    await expect(page.getByText('Uyğun elan tapılmadı.')).toBeVisible()
  })
})
