import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { ListingPublic } from '@/features/listings/types'

import { ListingDetailPage } from './ListingDetailPage'

const listing: ListingPublic = {
  shortId: 2001,
  slug: 'balta',
  canonicalPath: '/elan/balta-2001',
  title: 'Ov baltası',
  description: 'Yaxşı vəziyyətdə.',
  price: 90,
  currency: 'AZN',
  condition: 'Used',
  hasDelivery: false,
  brand: null,
  sellerType: 'Individual',
  categoryId: 12,
  categorySlug: 'balta',
  categoryNameAz: 'Balta',
  categoryPath: [],
  regionSlug: 'baki',
  regionNameAz: 'Bakı',
  attributes: [],
  media: [],
  sellerName: 'Test Satıcı',
  store: null,
  showPhone: true,
  contactPhoneMasked: '+994501***67',
  isFavorited: false,
  publishedAt: '2026-09-02T10:00:00+00:00',
  viewCount: 3,
}

function renderPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/elan/:slug" element={<ListingDetailPage />} />
    </Routes>,
    { route: '/elan/balta-2001' },
  )
}

/**
 * The Phase 5 age gate. A restricted listing is browsable, but the server refuses the page until
 * the visitor acknowledges — and the page turns that refusal into an interstitial rather than an
 * error screen.
 */
describe('ListingDetailPage age gate', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('shows an interstitial naming the category instead of an error', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/similar': () => jsonResponse([]),
      '/listings/by-short-id': () =>
        problemResponse(400, 'Yaş təsdiqi tələb olunur.', { ageConfirmation: ['Balta'] }),
    }))

    renderPage()

    expect(await screen.findByRole('heading', { name: 'Yaş təsdiqi' })).toBeInTheDocument()
    expect(screen.getByText(/Balta/)).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Ov baltası' })).not.toBeInTheDocument()
  })

  it('re-requests the page with the acknowledgement once given', async () => {
    // Routed on the acknowledgement itself rather than on a flag flipped by the test, so the
    // assertion does not depend on when the refetch happens to fire.
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/similar': () => jsonResponse([]),
      'ageConfirmed=true': () => jsonResponse(listing),
      '/listings/by-short-id': () =>
        problemResponse(400, 'Yaş təsdiqi tələb olunur.', { ageConfirmation: ['Balta'] }),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: '18 yaşım tamam olub' }))

    expect(await screen.findByRole('heading', { name: 'Ov baltası' })).toBeInTheDocument()
    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes('ageConfirmed=true')),
    ).toBe(true)
  })

  it('does not ask an ordinary listing for an acknowledgement', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/similar': () => jsonResponse([]),
      '/listings/by-short-id': () => jsonResponse(listing),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Ov baltası' })).toBeInTheDocument()
    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes('ageConfirmed=true')),
    ).toBe(false)
  })

  it('offers a report form that posts the chosen reason', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/similar': () => jsonResponse([]),
      '/report': () => new Response(null, { status: 204 }),
      '/listings/by-short-id': () => jsonResponse(listing),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Şikayət et' }))
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Səbəb' }), 'Fraud')
    await userEvent.click(screen.getByRole('button', { name: 'Göndər' }))

    expect(await screen.findByText(/qeydə alındı/)).toBeInTheDocument()

    const call = fetchMock.mock.calls.find(([url]) => String(url).includes('/report'))!
    const body = JSON.parse(String((call[1] as RequestInit).body)) as { reason: string }

    expect(body.reason).toBe('Fraud')
  })

  it('sends a signed-out visitor to sign in before saving', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/similar': () => jsonResponse([]),
      '/listings/by-short-id': () => jsonResponse(listing),
    }))

    renderPage()

    await screen.findByRole('heading', { name: 'Ov baltası' })

    const link = screen.getByRole('link', { name: 'Seçilmişlərə əlavə et' })

    expect(link).toHaveAttribute('href', '/giris')
  })
})
