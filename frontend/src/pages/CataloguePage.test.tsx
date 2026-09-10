import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { ListingCard, ListingSearchResult } from '@/features/listings/types'

import { CataloguePage } from './CataloguePage'

function card(overrides: Partial<ListingCard> = {}): ListingCard {
  return {
    shortId: 2001,
    slug: 'ov-bel-cantasi',
    path: '/elan/ov-bel-cantasi-2001',
    title: 'Ov bel çantası 30L',
    price: 150,
    currency: 'AZN',
    regionNameAz: 'Bakı',
    categorySlug: 'bel-cantasi',
    categoryNameAz: 'Bel çantası',
    imageUrl: null,
    hasDelivery: true,
    condition: 'Used',
    sellerType: 'Individual',
    requiresAgeConfirmation: false,
    isFavorited: false,
    publishedAt: '2026-09-02T10:00:00+00:00',
    ...overrides,
  }
}

function results(items: ListingCard[], overrides: Partial<ListingSearchResult> = {}): ListingSearchResult {
  return {
    items,
    page: 1,
    pageSize: 24,
    total: items.length,
    totalIsExact: true,
    totalPages: 1,
    sort: 'newest',
    ...overrides,
  }
}

function mockCatalogue(page: ListingSearchResult, extra: Record<string, () => Response> = {}) {
  const fetchMock = mockFetchByUrl({
    '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    '/regions': () => jsonResponse([{ id: 13, slug: 'baki', nameAz: 'Bakı', nameRu: null, listingCount: 0 }]),
    '/listings/facets': () => jsonResponse({ categories: [], regions: [] }),
    ...extra,
    '/listings': () => jsonResponse(page),
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderCatalogue(route = '/elanlar') {
  return renderWithProviders(
    <Routes>
      <Route path="/elanlar" element={<CataloguePage />} />
      <Route path="/elanlar/:categorySlug" element={<CataloguePage />} />
    </Routes>,
    { route },
  )
}

describe('CataloguePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders a grid of results and the total', async () => {
    mockCatalogue(results([card(), card({ shortId: 2002, title: 'Çadır 2 nəfərlik' })]))
    renderCatalogue()

    expect(await screen.findByText('Ov bel çantası 30L')).toBeInTheDocument()
    expect(screen.getByText('Çadır 2 nəfərlik')).toBeInTheDocument()
    expect(screen.getByText('2 elan')).toBeInTheDocument()
  })

  it('shows a capped total as an approximation rather than a wrong number', async () => {
    mockCatalogue(results([card()], { total: 10000, totalIsExact: false }))
    renderCatalogue()

    // Testing Library normalises the non-breaking space, so either separator satisfies this.
    expect(await screen.findByText(/^10\s000\+ elan$/)).toBeInTheDocument()
  })

  it('renders the three price cases the way the rest of the site does', async () => {
    mockCatalogue(results([
      card({ shortId: 1, price: null }),
      card({ shortId: 2, price: 0 }),
      card({ shortId: 3, price: 150 }),
    ]))

    renderCatalogue()

    expect(await screen.findByText('Razılaşma ilə')).toBeInTheDocument()
    expect(screen.getByText('Pulsuz')).toBeInTheDocument()
    expect(screen.getByText('150 ₼')).toBeInTheDocument()
  })

  it('marks a restricted listing on the card', async () => {
    mockCatalogue(results([card({ requiresAgeConfirmation: true })]))
    renderCatalogue()

    expect(await screen.findByText('Yaş təsdiqi')).toBeInTheDocument()
  })

  it('puts a chosen filter into the URL and asks the API for it', async () => {
    const fetchMock = mockCatalogue(results([card()]))
    renderCatalogue()

    await screen.findByText('Ov bel çantası 30L')
    await userEvent.type(screen.getAllByLabelText('min.')[0]!, '50')

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes('priceMin=50'))).toBe(true),
    )
  })

  it('reads the filters back out of the URL on first load', async () => {
    const fetchMock = mockCatalogue(results([card()]))
    renderCatalogue('/elanlar?priceMin=100&condition=New&sort=price_asc')

    await waitFor(() => {
      const called = fetchMock.mock.calls.map(([url]) => String(url)).join(' ')

      expect(called).toContain('priceMin=100')
      expect(called).toContain('condition=New')
      expect(called).toContain('sort=price_asc')
    })
  })

  it('sends the category from the path, not from a parameter', async () => {
    const fetchMock = mockCatalogue(results([card()]))
    renderCatalogue('/elanlar/bel-cantasi')

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) => String(url).includes('category=bel-cantasi')),
      ).toBe(true),
    )
  })

  it('offers relevance sorting only when there is a search term', async () => {
    mockCatalogue(results([card()]))
    const view = renderCatalogue()

    await screen.findByText('Ov bel çantası 30L')
    expect(screen.queryByRole('option', { name: 'Uyğunluğa görə' })).not.toBeInTheDocument()

    view.unmount()
    vi.unstubAllGlobals()

    mockCatalogue(results([card()]))
    renderCatalogue('/elanlar?q=cadir')

    expect(await screen.findByRole('option', { name: 'Uyğunluğa görə' })).toBeInTheDocument()
  })

  it('says so when nothing matched', async () => {
    mockCatalogue(results([]))
    renderCatalogue()

    expect(await screen.findByText('Uyğun elan tapılmadı.')).toBeInTheDocument()
  })

  it('surfaces a failure with a retry', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
        '/regions': () => jsonResponse([]),
        '/listings': () => problemResponse(500, 'Server xətası.'),
      }),
    )

    renderCatalogue()

    expect(await screen.findByText('Elanları yükləmək mümkün olmadı.')).toBeInTheDocument()
  })

  it('hides the save control from a signed-out visitor', async () => {
    mockCatalogue(results([card()]))
    renderCatalogue()

    await screen.findByText('Ov bel çantası 30L')

    expect(screen.queryByRole('button', { name: 'Seçilmişlərə əlavə et' })).not.toBeInTheDocument()
  })

  it('pages forward without losing the other filters', async () => {
    const fetchMock = mockCatalogue(results([card()], { totalPages: 3, page: 1 }))
    renderCatalogue('/elanlar?condition=New')

    await screen.findByText('Ov bel çantası 30L')
    await userEvent.click(screen.getByRole('button', { name: 'Növbəti' }))

    await waitFor(() => {
      const called = fetchMock.mock.calls.map(([url]) => String(url)).join(' ')

      expect(called).toContain('page=2')
      expect(called).toContain('condition=New')
    })
  })
})
