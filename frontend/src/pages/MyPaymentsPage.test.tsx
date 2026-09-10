import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { PaymentOrder } from '@/features/promotions/types'

import { MyPaymentsPage } from './MyPaymentsPage'

function order(overrides: Partial<PaymentOrder> = {}): PaymentOrder {
  return {
    id: '99999999-9999-9999-9999-999999999999',
    listingId: '11111111-1111-1111-1111-111111111111',
    promotionPackageId: 5,
    packageNameAz: 'İrəli çək — 7 gün',
    durationDays: 7,
    amountAzn: 2,
    currency: 'AZN',
    status: 'Paid',
    expiresAt: '2026-09-04T13:00:00+00:00',
    createdAt: '2026-09-04T12:30:00+00:00',
    ...overrides,
  }
}

function page(items: PaymentOrder[]) {
  return { items, page: 1, pageSize: 24, total: items.length, totalPages: 1 }
}

describe('MyPaymentsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('lists what the seller bought with the state the server confirmed', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/me/payment-orders': () =>
          jsonResponse(
            page([
              order(),
              order({ id: '88888888-8888-8888-8888-888888888888', status: 'PaidAfterExpiry', packageNameAz: 'İrəli çək — 3 gün', durationDays: 3, amountAzn: 1 }),
            ]),
          ),
      }),
    )

    renderWithProviders(<MyPaymentsPage />, { route: '/kabinet/odenisler' })

    const rows = await screen.findAllByTestId('my-payment-row')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('İrəli çək — 7 gün')
    expect(rows[0]).toHaveTextContent('7 gün')
    expect(rows[0]).toHaveTextContent('Ödənilib')
    expect(rows[0]).toHaveTextContent('2 ₼')
    // A late capture says a refund follows — never that the listing was promoted.
    expect(rows[1]).toHaveTextContent('Gec ödəniş · geri qaytarılacaq')
    expect(screen.getAllByRole('link', { name: 'Nəticəyə bax' })).toHaveLength(2)
  })

  it('points a seller with no orders back to their listings', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/me/payment-orders': () => jsonResponse(page([])),
      }),
    )

    renderWithProviders(<MyPaymentsPage />, { route: '/kabinet/odenisler' })

    expect(await screen.findByText('Hələ ödəniş yoxdur.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Mənim elanlarıma qayıt' })).toHaveAttribute('href', '/kabinet/elanlarim')
  })
})
