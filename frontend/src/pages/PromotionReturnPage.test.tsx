import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { PaymentOrder } from '@/features/promotions/types'

import { PromotionReturnPage } from './PromotionReturnPage'

const orderId = '99999999-9999-9999-9999-999999999999'

function routedPage() {
  return (
    <Routes>
      <Route path="/promotions/orders/:id/return" element={<PromotionReturnPage />} />
    </Routes>
  )
}

function order(overrides: Partial<PaymentOrder> = {}): PaymentOrder {
  return {
    id: orderId,
    listingId: '11111111-1111-1111-1111-111111111111',
    promotionPackageId: 5,
    packageNameAz: '7 günlük irəli çəkmə',
    durationDays: 7,
    amountAzn: 9.99,
    currency: 'AZN',
    status: 'AwaitingPayment',
    expiresAt: '2026-09-04T13:00:00+00:00',
    createdAt: '2026-09-04T12:30:00+00:00',
    ...overrides,
  }
}

describe('PromotionReturnPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('shows the verified activation, not the outcome the URL claims', async () => {
    // The redirect that landed here can carry any outcome query string — the page must render
    // what the server actually confirmed, never the query string on its own.
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      [`/me/payment-orders/${orderId}`]: () => jsonResponse(order({ status: 'Paid' })),
    }))

    renderWithProviders(routedPage(), {
      route: `/promotions/orders/${orderId}/return?outcome=success`,
    })

    const heading = await screen.findByTestId('promotion-return-status')
    expect(heading).toHaveTextContent('Elanınız irəli çəkildi')
    expect(heading).toHaveAttribute('data-status', 'Paid')
  })

  it('reports a failed payment honestly even when the URL claims success', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      [`/me/payment-orders/${orderId}`]: () => jsonResponse(order({ status: 'Failed' })),
    }))

    renderWithProviders(routedPage(), {
      // A forged or stale redirect could claim success; the server's own status still wins.
      route: `/promotions/orders/${orderId}/return?outcome=success`,
    })

    const heading = await screen.findByTestId('promotion-return-status')
    expect(heading).toHaveTextContent('Ödəniş uğursuz oldu')
    expect(heading).toHaveAttribute('data-status', 'Failed')
  })

  it('explains an expired order', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      [`/me/payment-orders/${orderId}`]: () => jsonResponse(order({ status: 'Expired' })),
    }))

    renderWithProviders(routedPage(), { route: `/promotions/orders/${orderId}/return` })

    expect(await screen.findByText(/Sifarişin vaxtı bitib/)).toBeInTheDocument()
  })

  it('explains a capture the gateway confirmed only after the order expired', async () => {
    // The approved rule: an expired order never activates a promotion. The seller still paid, so
    // the page must say so honestly — a refund follows, not a bump — and never claim activation.
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      [`/me/payment-orders/${orderId}`]: () => jsonResponse(order({ status: 'PaidAfterExpiry' })),
    }))

    renderWithProviders(routedPage(), {
      route: `/promotions/orders/${orderId}/return?outcome=success`,
    })

    const heading = await screen.findByTestId('promotion-return-status')
    expect(heading).toHaveTextContent('Ödəniş gec təsdiqləndi')
    expect(heading).toHaveAttribute('data-status', 'PaidAfterExpiry')
    expect(screen.getByText(/Məbləğ geri qaytarılacaq/)).toBeInTheDocument()
    expect(screen.queryByText(/Elanınız irəli çəkildi/)).not.toBeInTheDocument()
  })
})
