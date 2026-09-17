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
  slug: 'ov-bel-cantasi',
  canonicalPath: '/elan/ov-bel-cantasi-2001',
  title: 'Ov bel çantası 30L',
  description: 'Az istifadə olunub.',
  price: 150,
  currency: 'AZN',
  condition: 'Used',
  hasDelivery: true,
  brand: 'Deuter',
  sellerType: 'Individual',
  categoryId: 37,
  categorySlug: 'bel-cantasi',
  categoryNameAz: 'Bel çantası',
  categoryPath: [{ id: 35, slug: 'canta-ve-aksesuar', nameAz: 'Çanta və aksesuar' }],
  regionSlug: 'baki',
  regionNameAz: 'Bakı',
  attributes: [{ key: 'capacity_l', labelAz: 'Həcm', displayValue: '30 l' }],
  media: [],
  sellerName: 'Test Satıcı',
  store: null,
  showPhone: true,
  contactPhoneMasked: '+994501***67',
  isFavorited: false,
  publishedAt: '2026-09-02T10:00:00+00:00',
  viewCount: 12,
}

const storeListings = {
  items: [],
  page: 1,
  pageSize: 24,
  total: 0,
  totalIsExact: true,
  totalPages: 0,
  sort: 'newest',
}

function mockListing(overrides: Partial<ListingPublic> = {}, phoneStatus = 200, similar: unknown[] = []) {
  const fetchMock = mockFetchByUrl({
    '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    '/phone': () =>
      phoneStatus === 200
        ? jsonResponse({ contactPhone: '+994501234567' })
        : problemResponse(phoneStatus, 'Satıcı nömrəsini gizlədib.'),
    '/similar': () => jsonResponse(similar),
    '/stores/ovcu-dunyasi/listings': () => jsonResponse(storeListings),
    '/listings/by-short-id': () => jsonResponse({ ...listing, ...overrides }),
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const route = '/elan/ov-bel-cantasi-2001'

/** The page reads the listing number out of the path, so it needs a real route match. */
function renderPage() {
  return renderWithProviders(
    <Routes>
      <Route path="/elan/:slug" element={<ListingDetailPage />} />
    </Routes>,
    { route },
  )
}

describe('ListingDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders the price, the title and a location-first attribute table', async () => {
    mockListing()
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Ov bel çantası 30L' })).toBeInTheDocument()
    expect(screen.getByText('150 ₼')).toBeInTheDocument()

    const rows = screen.getAllByRole('term').map((node) => node.textContent)

    expect(rows[0]).toBe('Şəhər')
    expect(rows).toContain('Həcm')
    expect(screen.getByText('30 l')).toBeInTheDocument()
  })

  it('keeps the full phone number out of the page until it is asked for', async () => {
    const fetchMock = mockListing()
    renderPage()

    const reveal = await screen.findByRole('button', { name: /Nömrəni göstər/ })

    expect(reveal).toHaveTextContent('+994501***67')
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/phone'))).toBe(false)

    await userEvent.click(reveal)

    expect(await screen.findByRole('link', { name: '+994501234567' })).toBeInTheDocument()
  })

  it('says when the seller has hidden the number', async () => {
    mockListing({ showPhone: false, contactPhoneMasked: null })
    renderPage()

    expect(await screen.findByText('Satıcı nömrəsini gizlədib.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Nömrəni göstər/ })).not.toBeInTheDocument()
  })

  it('shows a missing price as negotiable and a zero price as free', async () => {
    mockListing({ price: null })
    const view = renderPage()

    expect(await screen.findByText('Razılaşma ilə')).toBeInTheDocument()

    view.unmount()
    vi.unstubAllGlobals()

    mockListing({ price: 0 })
    renderPage()

    expect(await screen.findByText('Pulsuz')).toBeInTheDocument()
  })

  it('answers a removed listing as not found, not as something going wrong', async () => {
    // Sold, expired, withdrawn, never existed — to whoever followed the link these are one thing,
    // and none of them is a failure worth retrying.
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/listings/by-short-id': () =>
          problemResponse(404, 'Bu nömrəli elan mövcud deyil və ya ləğv olunub.'),
      }),
    )

    renderPage()

    expect(await screen.findByText('Axtardığınız elan mövcud deyil')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Bütün elanlara bax' })).toHaveAttribute(
      'href',
      '/elanlar',
    )

    expect(screen.queryByText('Nəsə səhv getdi')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Yenidən cəhd et' })).not.toBeInTheDocument()
  })

  it('offers a retry when the request itself failed', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/listings/by-short-id': () => problemResponse(500, 'Server xətası.'),
      }),
    )

    renderPage()

    expect(await screen.findByRole('button', { name: 'Yenidən cəhd et' })).toBeInTheDocument()
    expect(screen.queryByText('Axtardığınız elan mövcud deyil')).not.toBeInTheDocument()
  })

  it('renders the description as text, never as markup', async () => {
    mockListing({ description: '<img src=x onerror="alert(1)"> qalan mətn' })
    renderPage()

    expect(await screen.findByText(/<img src=x onerror="alert\(1\)"> qalan mətn/)).toBeInTheDocument()
    expect(document.querySelector('img[onerror]')).toBeNull()
  })
})

/**
 * The storefront boundary as it reaches the buyer: a live store is named and linked, a suspended
 * one sends no block at all, and the strip at the foot of the page follows the same rule.
 */
describe('ListingDetailPage and storefronts', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  const store = { slug: 'ovcu-dunyasi', name: 'Ovçu Dünyası', isVerified: true, logoUrl: null }

  const similarCard = {
    shortId: 2002,
    slug: 'ikinci-canta',
    path: '/elan/ikinci-canta-2002',
    title: 'İkinci çanta',
    price: 120,
    currency: 'AZN',
    regionNameAz: 'Bakı',
    categorySlug: 'bel-cantasi',
    categoryNameAz: 'Bel çantası',
    imageUrl: null,
    hasDelivery: false,
    condition: 'Used',
    sellerType: 'Individual',
    requiresAgeConfirmation: false,
    isFavorited: false,
    publishedAt: '2026-09-01T10:00:00+00:00',
  }

  it('names and links the storefront a listing belongs to', async () => {
    mockListing({ store, sellerType: 'Store' })
    renderPage()

    const link = await screen.findByRole('link', { name: /Ovçu Dünyası/ })

    expect(link).toHaveAttribute('href', '/magaza/ovcu-dunyasi')
  })

  it('replaces the similar-listings strip with the storefront grid', async () => {
    mockListing({ store, sellerType: 'Store' })
    renderPage()

    expect(await screen.findByText(/Mağazanın elanları/)).toBeInTheDocument()
    expect(screen.queryByText('Bənzər elanlar')).not.toBeInTheDocument()
  })

  it('falls back to the seller name when the storefront is no longer public', async () => {
    // D-5: a suspended store sends no block, and the server keeps sending sellerName precisely so
    // the page still has a name to show. The listing itself is still on sale.
    mockListing({ store: null, sellerType: 'Store', sellerName: 'Test Satıcı' })
    renderPage()

    expect(await screen.findByText('Test Satıcı')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Ovçu Dünyası/ })).not.toBeInTheDocument()
    expect(screen.queryByText(/Mağazanın elanları/)).not.toBeInTheDocument()
  })

  it('never shows an owner name for a live storefront', async () => {
    // The server withholds it entirely for a store listing; the page must not be relying on
    // merely hiding it.
    mockListing({ store, sellerType: 'Store', sellerName: null })
    renderPage()

    await screen.findByRole('link', { name: /Ovçu Dünyası/ })

    expect(screen.queryByText('Test Satıcı')).not.toBeInTheDocument()
  })

  it('keeps showing similar listings for an individual seller', async () => {
    mockListing({}, 200, [similarCard])
    renderPage()

    expect(await screen.findByText('Bənzər elanlar')).toBeInTheDocument()
  })
})