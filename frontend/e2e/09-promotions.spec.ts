import type { APIRequestContext, Page } from '@playwright/test'

import { testIds } from '@/testIds'

import {
  expect,
  goto,
  grantRole,
  login,
  otpFor,
  register,
  signInAsOperator,
  test,
  uniquePhone,
  uniqueTitle,
} from './fixtures'
import { approveListing, createListing } from './journeys'

const API = 'http://localhost:5080/api/v1'

/**
 * Flow 10 — paid listing promotions, against the real backend and the Development-only simulated
 * gateway (see backend/src/Ovcuprim.Infrastructure/Payments/DevelopmentPaymentGatewayClient.cs). No
 * real payment provider is ever called: the "bank" page these tests click through is the API's own
 * dev-only checkout stand-in, and every callback that follows goes through the real signature
 * verification, idempotency and activation code a production delivery would.
 */

/** Registers via the API only, bypassing the UI — for accounts these tests act as but never click through. */
async function apiRegisterAndSignIn(request: APIRequestContext, phone: string, fullName: string): Promise<string> {
  await request.post(`${API}/auth/register`, { data: { phoneNumber: phone, fullName } })
  const code = await otpFor(request, phone)
  const response = await request.post(`${API}/auth/verify`, {
    data: { phoneNumber: phone, code, purpose: 0 },
  })

  const body = (await response.json()) as { accessToken: string }
  return body.accessToken
}

/**
 * Registers, grants Admin in the database, then verifies — the role has to be set before the token
 * is minted, since the token itself carries the role as a claim rather than the API re-reading it
 * on every request.
 */
async function apiRegisterAdminAndSignIn(request: APIRequestContext, phone: string, fullName: string): Promise<string> {
  await request.post(`${API}/auth/register`, { data: { phoneNumber: phone, fullName } })
  await grantRole(phone, 'Admin')

  const code = await otpFor(request, phone)
  const response = await request.post(`${API}/auth/verify`, {
    data: { phoneNumber: phone, code, purpose: 0 },
  })

  const body = (await response.json()) as { accessToken: string }
  return body.accessToken
}

/**
 * Both the code and the display name are unique per call — the dev database is shared and never
 * reset between runs, so a fixed display name would eventually match more than one package and make
 * `startPurchase`'s selection ambiguous.
 */
async function createPromotionPackage(
  request: APIRequestContext,
  adminToken: string,
  priceAzn = 5,
): Promise<{ id: number; nameAz: string }> {
  const unique = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`
  const nameAz = `E2E irəli çəkmə ${unique}`

  const response = await request.post(`${API}/admin/promotion-packages`, {
    headers: { Authorization: `Bearer ${adminToken}` },
    data: {
      code: `e2e-bump-${unique}`,
      nameAz,
      descriptionAz: null,
      durationDays: 7,
      priceAzn,
      sortOrder: 1,
    },
  })

  expect(response.ok()).toBe(true)
  const created = (await response.json()) as { id: number }

  return { id: created.id, nameAz }
}

/** The listing id from "Mənim elanlarım", read off the row's own edit link. */
async function listingIdByTitle(page: Page, title: string): Promise<string> {
  await goto(page, '/kabinet/elanlarim')
  const row = page.getByTestId(testIds.myListingRow).filter({ hasText: title })
  const href = await row.getByRole('link', { name: 'Düzəliş et' }).getAttribute('href')

  return href!.match(/elanlarim\/([^/]+)\/duzelis/)![1]!
}

/**
 * A live, moderator-approved listing (the promote button only appears on an Active listing), with
 * the browser left signed in as the seller. Registers its own moderator account and clears cookies
 * twice along the way — the same dance `publishApprovedListing` in journeys.ts uses, inlined here
 * because that helper leaves the browser signed in as the operator, not the seller these tests need.
 */
async function sellerWithApprovedListing(page: Page, title: string): Promise<string> {
  const sellerPhone = uniquePhone('55')
  const moderatorPhone = uniquePhone('60')

  await register(page, sellerPhone, 'Promosyon Satıcısı')
  await createListing(page, title, sellerPhone)

  await page.context().clearCookies()
  await register(page, moderatorPhone, 'Promosyon Moderatoru')
  await grantRole(moderatorPhone, 'Admin')
  await page.context().clearCookies()
  await signInAsOperator(page, moderatorPhone)
  await approveListing(page, title)

  await page.context().clearCookies()
  await login(page, sellerPhone)

  return sellerPhone
}

/** Opens the dialog on the seller's own listings page, picks the given package, and starts checkout. */
async function startPurchase(page: Page, title: string, packageName: string): Promise<void> {
  await goto(page, '/kabinet/elanlarim')
  await page.getByTestId(testIds.myListingRow).filter({ hasText: title }).getByRole('button', { name: 'İrəli çək' }).click()
  await expect(page.getByRole('dialog', { name: 'Elanı irəli çək' })).toBeVisible()
  await page.getByText(packageName).click()
  await page.getByRole('button', { name: 'Ödənişə keç' }).click()

  // A real navigation away from the SPA, to the dev-only checkout stand-in.
  await page.waitForURL(/\/api\/v1\/dev\/payments\/.+\/checkout/, { timeout: 20_000 })
}

function orderIdFromCheckoutUrl(page: Page): string {
  return page.url().match(/dev\/payments\/([^/]+)\/checkout/)![1]!
}

test.describe('Paid listing promotions', () => {
  test.describe.configure({ timeout: 240_000 })

  test('create order, checkout, a verified callback activates the promotion', async ({ page, request }) => {
    const title = uniqueTitle('Promosyon')
    await sellerWithApprovedListing(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    await page.getByTestId('dev-payment-succeed').click()

    await page.waitForURL(/\/promotions\/orders\/.+\/return/, { timeout: 20_000 })
    const status = page.getByTestId(testIds.promotionReturnStatus)
    await expect(status).toHaveText('Elanınız irəli çəkildi', { timeout: 20_000 })
    await expect(status).toHaveAttribute('data-status', 'Paid')

    // The seller's own list now says so — and offers nothing more to buy on that listing while
    // the promotion runs (one at a time).
    await goto(page, '/kabinet/elanlarim')
    const row = page.getByTestId(testIds.myListingRow).filter({ hasText: title })
    await expect(row.getByTestId(testIds.promotionStatus)).toContainText('İrəli çəkilib', { timeout: 20_000 })
    await expect(row.getByRole('button', { name: 'İrəli çək' })).toHaveCount(0)

    // And it is on the seller's own payment history.
    await goto(page, '/kabinet/odenisler')
    await expect(page.getByTestId(testIds.myPaymentRow).filter({ hasText: pkg.nameAz })).toContainText('Ödənilib', {
      timeout: 20_000,
    })
  })

  test('an operator finds the order on the payments page, reads its ledger and refunds it', async ({ page, request }) => {
    const title = uniqueTitle('Refund')
    const sellerPhone = await sellerWithApprovedListing(page, title)

    const adminPhone = uniquePhone('70')
    const adminToken = await apiRegisterAdminAndSignIn(request, adminPhone, 'Ödəniş Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    const orderId = orderIdFromCheckoutUrl(page)
    await page.getByTestId('dev-payment-succeed').click()

    await page.waitForURL(/\/promotions\/orders\/.+\/return/, { timeout: 20_000 })
    await expect(page.getByTestId(testIds.promotionReturnStatus)).toHaveAttribute('data-status', 'Paid', {
      timeout: 20_000,
    })

    // The operator searches by the gateway's own reference — what its dashboard would show them.
    await page.context().clearCookies()
    await signInAsOperator(page, adminPhone)
    await goto(page, `/admin/payments?q=${encodeURIComponent(`dev-${orderId}`)}`)

    const row = page.getByTestId(testIds.queueRow).filter({ hasText: pkg.nameAz })
    await expect(row).toHaveCount(1, { timeout: 30_000 })
    await row.getByRole('button', { name: 'Aç' }).click()

    const pane = page.getByRole('region', { name: 'Sifariş detalları' })
    await expect(pane.getByText('Status yoxlanıldı')).toBeVisible({ timeout: 20_000 })
    await pane.getByRole('button', { name: 'Geri qaytar' }).click()

    const dialog = page.getByRole('dialog', { name: 'Ödənişi geri qaytar' })
    await dialog.getByLabel('Səbəb *').fill('E2E: geri qaytarma yoxlaması')
    await dialog.getByRole('button', { name: 'Geri qaytar' }).click()

    await expect(page.getByText('Geri qaytarma icra edildi')).toBeVisible({ timeout: 30_000 })
    await expect(row).toContainText('Geri qaytarılıb', { timeout: 30_000 })

    // The promotion is gone from the seller's side, and the listing can be promoted again.
    await page.context().clearCookies()
    await login(page, sellerPhone)
    await goto(page, '/kabinet/elanlarim')
    const listingRow = page.getByTestId(testIds.myListingRow).filter({ hasText: title })
    await expect(listingRow.getByRole('button', { name: 'İrəli çək' })).toBeVisible({ timeout: 20_000 })
    await expect(listingRow.getByTestId(testIds.promotionStatus)).toHaveCount(0)
  })

  test('a failed payment never activates the promotion', async ({ page, request }) => {
    const title = uniqueTitle('Ugursuz')
    await sellerWithApprovedListing(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    await page.getByTestId('dev-payment-fail').click()

    await page.waitForURL(/\/promotions\/orders\/.+\/return/, { timeout: 20_000 })
    const status = page.getByTestId(testIds.promotionReturnStatus)
    await expect(status).toHaveText('Ödəniş uğursuz oldu', { timeout: 20_000 })
    await expect(status).toHaveAttribute('data-status', 'Failed')
  })

  test('an expired order cannot activate a promotion even with a confirming callback', async ({ page, request }) => {
    const title = uniqueTitle('Muddeti')
    await sellerWithApprovedListing(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    const orderId = orderIdFromCheckoutUrl(page)

    // Development-only: forces the order past its 30-minute window without waiting for it, so the
    // rule can be exercised in seconds — see PromotionMaintenanceService.
    const expired = await request.post(`${API}/dev/payments/${orderId}/expire`)
    expect(expired.ok()).toBe(true)

    // The gateway still confirms payment — a late callback is exactly the scenario this rule guards.
    // Nothing activates; the capture is recorded as PaidAfterExpiry so it can be refunded rather
    // than left looking unpaid (integration audit M-1).
    await page.getByTestId('dev-payment-succeed').click()

    await page.waitForURL(/\/promotions\/orders\/.+\/return/, { timeout: 20_000 })
    await expect(page.getByTestId(testIds.promotionReturnStatus)).toHaveAttribute('data-status', 'PaidAfterExpiry', {
      timeout: 20_000,
    })
  })

  test('a duplicate callback delivery is handled without error and does not double-activate', async ({ page, request }) => {
    const title = uniqueTitle('Ikili')
    await sellerWithApprovedListing(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    const orderId = orderIdFromCheckoutUrl(page)

    // The same simulated outcome, delivered twice — exactly what a real gateway's retry looks like.
    const first = await request.post(`${API}/dev/payments/${orderId}/simulate?outcome=success`)
    const second = await request.post(`${API}/dev/payments/${orderId}/simulate?outcome=success`)

    expect(first.ok()).toBe(true)
    expect(second.ok()).toBe(true)

    await goto(page, `/promotions/orders/${orderId}/return`)
    await expect(page.getByTestId(testIds.promotionReturnStatus)).toHaveAttribute('data-status', 'Paid', {
      timeout: 20_000,
    })
  })

  test('a forged callback signature is rejected', async ({ request }) => {
    const response = await request.post(`${API}/payments/callback/epoint`, {
      form: {
        data: Buffer.from(
          JSON.stringify({ order_id: '00000000-0000-0000-0000-000000000000', status: 'success' }),
        ).toString('base64'),
        signature: 'not-a-real-signature',
      },
    })

    expect(response.status()).toBe(400)
  })

  test('a cross-user purchase attempt is rejected', async ({ page, request }) => {
    const title = uniqueTitle('Basqasi')
    await sellerWithApprovedListing(page, title)
    const listingId = await listingIdByTitle(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    const otherToken = await apiRegisterAndSignIn(request, uniquePhone('51'), 'Yad İstifadəçi')

    const response = await request.post(`${API}/me/listings/${listingId}/promotions/orders`, {
      headers: { Authorization: `Bearer ${otherToken}` },
      data: { packageId: pkg.id },
    })

    expect(response.status()).toBe(404)
  })

  test('a refund reverses the promotion and a new purchase becomes possible again', async ({ page, request }) => {
    const title = uniqueTitle('Geri')
    await sellerWithApprovedListing(page, title)

    const adminToken = await apiRegisterAdminAndSignIn(request, uniquePhone('70'), 'Paket Admini')
    const pkg = await createPromotionPackage(request, adminToken)

    await startPurchase(page, title, pkg.nameAz)
    const orderId = orderIdFromCheckoutUrl(page)
    await page.getByTestId('dev-payment-succeed').click()
    await page.waitForURL(/\/promotions\/orders\/.+\/return/, { timeout: 20_000 })
    await expect(page.getByTestId(testIds.promotionReturnStatus)).toHaveAttribute('data-status', 'Paid', {
      timeout: 20_000,
    })

    // Blocked while the promotion is active: the row says so and offers nothing more to buy — one
    // promotion at a time is shown as a fact, not discovered as an error after a click.
    await goto(page, '/kabinet/elanlarim')
    const promotedRow = page.getByTestId(testIds.myListingRow).filter({ hasText: title })
    await expect(promotedRow.getByTestId(testIds.promotionStatus)).toContainText('İrəli çəkilib', { timeout: 20_000 })
    await expect(promotedRow.getByRole('button', { name: 'İrəli çək' })).toHaveCount(0)

    const refund = await request.post(`${API}/admin/payment-orders/${orderId}/refund`, {
      headers: { Authorization: `Bearer ${adminToken}` },
      data: { amount: null, reason: 'E2E: geri qaytarma testi' },
    })
    expect(refund.ok()).toBe(true)

    // No longer blocked: a fresh purchase for the same listing succeeds.
    await goto(page, '/kabinet/elanlarim')
    await page.getByTestId(testIds.myListingRow).filter({ hasText: title }).getByRole('button', { name: 'İrəli çək' }).click()
    await page.getByText(pkg.nameAz).click()
    await page.getByRole('button', { name: 'Ödənişə keç' }).click()
    await page.waitForURL(/\/api\/v1\/dev\/payments\/.+\/checkout/, { timeout: 20_000 })
  })
})
