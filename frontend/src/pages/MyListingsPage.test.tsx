import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { ListingCapabilities, ListingSummary } from '@/features/listings/types'

import { MyListingsPage } from './MyListingsPage'

function capabilities(overrides: Partial<ListingCapabilities> = {}): ListingCapabilities {
  return { edit: false, publish: false, delete: false, restore: false, markSold: false, ...overrides }
}

function summary(overrides: Partial<ListingSummary> = {}): ListingSummary {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    shortId: 2001,
    slug: 'ov-bel-cantasi',
    status: 'Active',
    rejectionReason: null,
    title: 'Ov bel çantası 30L',
    price: 150,
    currency: 'AZN',
    regionNameAz: 'Bakı',
    categoryNameAz: 'Bel çantası',
    primaryImageUrl: null,
    mediaCount: 1,
    publishedAt: '2026-09-02T10:00:00+00:00',
    expiresAt: '2026-10-02T10:00:00+00:00',
    restorableUntil: null,
    viewCount: 12,
    createdAt: '2026-09-02T09:00:00+00:00',
    can: capabilities({ edit: true, delete: true, markSold: true }),
    promotion: null,
    ...overrides,
  }
}

function page(items: ListingSummary[]) {
  return { items, page: 1, pageSize: 24, total: items.length, totalPages: 1 }
}

function mockListings(items: ListingSummary[]) {
  const fetchMock = mockFetchByUrl({
    '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    '/me/listings': () => jsonResponse(page(items)),
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('MyListingsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('offers the seller buckets as tabs, with the live one first', async () => {
    mockListings([summary()])
    renderWithProviders(<MyListingsPage />)

    await screen.findByText('Ov bel çantası 30L')

    const tabs = [
      'Hazırda saytda',
      'Gözləmədə',
      'Dərc olunmamış',
      'Bloklanıb',
      'Müddəti başa çatmış',
      'Satılıb',
      'Qaralama',
    ]

    for (const tab of tabs) {
      expect(screen.getByRole('button', { name: tab })).toBeInTheDocument()
    }
  })

  it('asks the API for the bucket named in the URL', async () => {
    const fetchMock = mockListings([])
    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=rejected' })

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes('status=rejected'))).toBe(true),
    )
  })

  it('shows the rejection reason on a listing that was not published', async () => {
    mockListings([
      summary({
        status: 'Rejected',
        rejectionReason: 'Şəkil məhsula aid deyil.',
        can: capabilities({ edit: true, delete: true }),
      }),
    ])

    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=rejected' })

    expect(await screen.findByText(/Şəkil məhsula aid deyil\./)).toBeInTheDocument()
  })

  it('shows a blocked listing with its reason and no self-service action', async () => {
    // Blocked is its own state, not a stand-in for Rejected: the seller can see why, but — unlike a
    // rejection — cannot edit their way back onto the site. The server sends every capability as
    // false, and the row has nothing to offer beyond what happened.
    mockListings([
      summary({
        status: 'Blocked',
        rejectionReason: 'Qaydalara zidd məzmun.',
        can: capabilities(),
      }),
    ])

    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=blocked' })

    await screen.findByText('Ov bel çantası 30L')

    expect(screen.getByText(/Qaydalara zidd məzmun\./)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ov bel çantası 30L' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Düzəliş et' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Elanı sil' })).not.toBeInTheDocument()
  })

  it('offers restore, and the deadline, only on an expired listing', async () => {
    mockListings([
      summary({
        status: 'Expired',
        expiresAt: '2026-09-02T10:00:00+00:00',
        restorableUntil: '2026-10-02T10:00:00+00:00',
        can: capabilities({ restore: true, markSold: true }),
      }),
    ])

    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=expired' })

    expect(await screen.findByRole('button', { name: 'Bərpa et' })).toBeInTheDocument()
    expect(screen.getByText(/Bərpa müddəti/)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Düzəliş et' })).not.toBeInTheDocument()
  })

  it('renders only the actions the server says are available', async () => {
    mockListings([summary({ status: 'Sold', can: capabilities() })])

    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=sold' })

    await screen.findByText('Ov bel çantası 30L')

    expect(screen.queryByRole('button', { name: 'Elanı sil' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Satıldı' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bərpa et' })).not.toBeInTheDocument()
  })

  it('sends a delete when the seller removes a listing', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/me/listings': () => jsonResponse(page([summary()])),
      '/listings/11111111-1111-1111-1111-111111111111/delete': () => new Response(null, { status: 204 }),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<MyListingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Elanı sil' }))

    // A POST, not a DELETE: for a published listing this retires it and stays undoable.
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).endsWith('/delete') && (init as RequestInit | undefined)?.method === 'POST',
        ),
      ).toBe(true),
    )
  })

  it('shows the price rules the seller chose', async () => {
    mockListings([
      summary({ price: null }),
      summary({ id: '22222222-2222-2222-2222-222222222222', shortId: 2002, price: 0 }),
    ])

    renderWithProviders(<MyListingsPage />)

    expect(await screen.findByText('Razılaşma ilə')).toBeInTheDocument()
    expect(screen.getByText('Pulsuz')).toBeInTheDocument()
  })

  it('says so when a bucket is empty', async () => {
    mockListings([])
    renderWithProviders(<MyListingsPage />)

    expect(await screen.findByText(/bölməsində elan yoxdur/)).toBeInTheDocument()
  })

  it('offers "İrəli çək" only on an active listing', async () => {
    mockListings([summary({ status: 'Sold', can: capabilities() })])
    renderWithProviders(<MyListingsPage />, { route: '/kabinet/elanlarim?status=sold' })

    await screen.findByText('Ov bel çantası 30L')
    expect(screen.queryByRole('button', { name: 'İrəli çək' })).not.toBeInTheDocument()
  })

  it('opens the package dialog and starts a purchase with only a package id', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      // More specific fragments first: matching is by substring in insertion order, and
      // "/me/listings" would otherwise also match the order-creation URL below it.
      '/promotions/orders': () =>
        jsonResponse({ paymentOrderId: '99999999-9999-9999-9999-999999999999', redirectUrl: 'https://gateway.test/pay/abc' }),
      '/promotion-packages': () =>
        jsonResponse([
          { id: 5, code: 'bump-7d', nameAz: '7 günlük irəli çəkmə', descriptionAz: null, durationDays: 7, priceAzn: 9.99, currency: 'AZN', bumpIntervalHours: 8 },
        ]),
      '/me/listings': () => jsonResponse(page([summary()])),
    })

    vi.stubGlobal('fetch', fetchMock)

    // jsdom's window.location.assign is non-configurable, so it cannot be spied on directly —
    // the whole location object is replaced with a stand-in for the duration of this test.
    const originalLocation = window.location
    const assign = vi.fn()
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...originalLocation, assign },
    })

    renderWithProviders(<MyListingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'İrəli çək' }))
    await userEvent.click(await screen.findByText('7 günlük irəli çəkmə'))
    await userEvent.click(screen.getByRole('button', { name: 'Ödənişə keç' }))

    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://gateway.test/pay/abc'))

    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation })

    const orderCall = fetchMock.mock.calls.find(([url]) => String(url).includes('/promotions/orders'))
    expect(orderCall).toBeDefined()
    const body = JSON.parse((orderCall![1] as RequestInit).body as string) as Record<string, unknown>
    // The only thing the client ever sends is which package was chosen — never a price or amount.
    expect(body).toEqual({ packageId: 5 })
  })

  it('shows a running promotion instead of the buy button, with when it ends and how often it lifts', async () => {
    mockListings([
      summary({
        promotion: {
          status: 'Active',
          activatedAt: '2026-09-05T10:00:00+00:00',
          expiresAt: '2026-09-12T10:00:00+00:00',
          bumpIntervalHours: 8,
        },
      }),
    ])
    renderWithProviders(<MyListingsPage />)

    await screen.findByText('Ov bel çantası 30L')

    const status = screen.getByTestId('promotion-status')
    expect(status).toHaveTextContent('İrəli çəkilib')
    expect(status).toHaveTextContent('hər 8 saatdan bir yenilənir')
    expect(screen.queryByRole('button', { name: 'İrəli çək' })).not.toBeInTheDocument()
  })

  it('tells the seller the server’s own reason when a purchase is refused', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/promotions/orders': () => problemResponse(409, 'Bu elan artıq irəli çəkilib.'),
        '/promotion-packages': () =>
          jsonResponse([
            { id: 5, code: 'bump-7d', nameAz: '7 günlük irəli çəkmə', descriptionAz: null, durationDays: 7, priceAzn: 9.99, currency: 'AZN', bumpIntervalHours: 8 },
          ]),
        '/me/listings': () => jsonResponse(page([summary()])),
      }),
    )

    renderWithProviders(<MyListingsPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'İrəli çək' }))
    // What the duration actually buys is said next to it, not left to the package name.
    expect(await screen.findByText('7 gün · hər 8 saatdan bir irəli çəkilir')).toBeInTheDocument()
    expect(screen.getByText(/ödəniş geri qaytarılmır/)).toBeInTheDocument()

    await userEvent.click(screen.getByText('7 günlük irəli çəkmə'))
    await userEvent.click(screen.getByRole('button', { name: 'Ödənişə keç' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Bu elan artıq irəli çəkilib.')
  })
})
