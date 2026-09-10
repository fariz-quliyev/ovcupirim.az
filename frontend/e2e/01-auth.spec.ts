import { expect, goto, login, register, test, uniquePhone } from './fixtures'

/**
 * Flow 1 — register, verify, sign out, sign in.
 *
 * Nothing else in the product is reachable without this, so it runs first and its failure is the
 * only one worth reading before the rest.
 */
test.describe('Authentication', () => {
  test('a new seller can register with a one-time code', async ({ page }) => {
    const phone = uniquePhone()

    await register(page, phone, 'Yeni Satıcı')

    await goto(page, '/kabinet')

    await expect(page.getByRole('heading', { name: 'Şəxsi kabinet' })).toBeVisible()
    await expect(page.getByText(phone)).toBeVisible()
  })

  test('a registered seller can sign out and back in', async ({ page }) => {
    const phone = uniquePhone()

    await register(page, phone, 'Qayıdan Satıcı')

    await goto(page, '/kabinet')
    await page.getByRole('button', { name: 'Çıxış' }).click()

    await expect(page.getByRole('link', { name: 'Giriş' })).toBeVisible()

    await login(page, phone)

    // Asserted on the account page rather than the header: the header's identity link renders once
    // the boot-time session refresh settles, so it is the slower and less meaningful signal.
    await goto(page, '/kabinet')

    await expect(page.getByRole('heading', { name: 'Şəxsi kabinet' })).toBeVisible()
    await expect(page.getByText(phone)).toBeVisible()
  })

  test('an account screen is not reachable while signed out', async ({ page }) => {
    await page.goto('/kabinet/elanlarim')

    // The guard sends them to sign in; the API would refuse regardless.
    await expect(page).toHaveURL(/\/giris/)
  })
})
