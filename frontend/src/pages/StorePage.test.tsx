import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from '@/features/auth/authTestUtils'
import type { ListingCard } from '@/features/listings/types'
import type { StoreCard, StorePublic } from '@/features/stores/types'

import { StorePage } from './StorePage'
import { StoresPage } from './StoresPage'

const store: StorePublic = {
  slug: 'ovcu-dunyasi',
  name: 'Ovçu Dünyası',
  description: 'Ov və kamp avadanlıqları.',
  address: 'Bakı ş.',
  isVerified: true,
  logoUrl: null,
  bannerUrl: null,
  listingCount: 12,
  followerCount: 4,
  showPhone: true,
  phoneMasked: '+994 50 *** ** **',
  isFollowing: false,
  memberSince: '2026-01-15T10:00:00+00:00',
}

const card: StoreCard = {
  slug: 'ovcu-dunyasi',
  name: 'Ovçu Dünyası',
  logoUrl: null,
  isVerified: true,
  listingCount: 12,
  followerCount: 4,
  memberSince: '2026-01-15T10:00:00+00:00',
}

const listing: ListingCard = {
  shortId: 3001,
  slug: 'ov-bicagi',
  path: '/elan/ov-bicagi-3001',
  title: 'Ov bıçağı',
  price: 90,
  currency: 'AZN',
  regionNameAz: 'Bakı',
  categorySlug: 'bicaq',
  categoryNameAz: 'Bıçaq',
  imageUrl: null,
  hasDelivery: true,
  condition: 'New',
  sellerType: 'Store',
  requiresAgeConfirmation: false,
  isFavorited: false,
  publishedAt: '2026-09-01T10:00:00+00:00',
}

const searchResult = {
  items: [listing],
  page: 1,
  pageSize: 24,
  total: 1,
  totalIsExact: true,
  totalPages: 1,
  sort: 'newest',
}

const anonymous = () => problemResponse(401, 'Sessiya tapılmadı.')

/** The page reads its slug from the path, so the route has to be real. */
function renderStore(slug: string) {
  return renderWithProviders(
    <Routes>
      <Route path="/magaza/:slug" element={<StorePage />} />
    </Routes>,
    { route: `/magaza/${slug}` },
  )
}

describe('StorePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('shows the storefront and its own listings', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': anonymous,
        '/stores/ovcu-dunyasi/listings': () => jsonResponse(searchResult),
        '/stores/ovcu-dunyasi': () => jsonResponse(store),
      }),
    )

    renderStore('ovcu-dunyasi')

    expect(await screen.findByRole('heading', { name: 'Ovçu Dünyası' })).toBeInTheDocument()
    expect(await screen.findByText('Ov bıçağı')).toBeInTheDocument()
    expect(screen.getByText(/12 elan · 4 izləyici/)).toBeInTheDocument()
  })

  it('reports a storefront that is not public as missing', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': anonymous,
        '/stores/dayandirilmis': () => problemResponse(404, 'Belə mağaza mövcud deyil.'),
      }),
    )

    renderStore('dayandirilmis')

    expect(await screen.findByText('Mağaza tapılmadı')).toBeInTheDocument()
  })

  it('keeps the phone number out of the page until it is asked for', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': anonymous,
      '/stores/ovcu-dunyasi/phone': () => jsonResponse({ phone: '+994501234567' }),
      '/stores/ovcu-dunyasi/listings': () => jsonResponse(searchResult),
      '/stores/ovcu-dunyasi': () => jsonResponse(store),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderStore('ovcu-dunyasi')

    const reveal = await screen.findByRole('button', { name: /Nömrəni göstər/ })
    expect(screen.queryByText('+994501234567')).not.toBeInTheDocument()

    await userEvent.click(reveal)

    expect(await screen.findByText('+994501234567')).toBeInTheDocument()
  })

  it('sends a signed-out visitor to sign in rather than following', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': anonymous,
        '/stores/ovcu-dunyasi/listings': () => jsonResponse(searchResult),
        '/stores/ovcu-dunyasi': () => jsonResponse(store),
      }),
    )

    renderStore('ovcu-dunyasi')

    const follow = await screen.findByRole('button', { name: 'İzlə' })

    expect(follow.closest('a')).toHaveAttribute('href', '/giris')
  })

  it('follows with a PUT once the visitor is signed in', async () => {
    setAccessToken(testAuthResponse.accessToken)

    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => jsonResponse(testAuthResponse),
      '/auth/me': () => jsonResponse(testAuthResponse.user),
      '/stores/ovcu-dunyasi/follow': () => new Response(null, { status: 204 }),
      '/stores/ovcu-dunyasi/listings': () => jsonResponse(searchResult),
      '/stores/ovcu-dunyasi': () => jsonResponse(store),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderStore('ovcu-dunyasi')

    await userEvent.click(await screen.findByRole('button', { name: 'İzlə' }))

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).endsWith('/stores/ovcu-dunyasi/follow') &&
            (init as RequestInit | undefined)?.method === 'PUT',
        ),
      ).toBe(true),
    )
  })
})

describe('StoresPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('lists active storefronts', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': anonymous,
        '/categories': () => jsonResponse([]),
        '/stores': () => jsonResponse({ items: [card], page: 1, pageSize: 24, total: 1, totalPages: 1 }),
      }),
    )

    renderWithProviders(<StoresPage />, { route: '/magazalar' })

    expect(await screen.findByText('Ovçu Dünyası')).toBeInTheDocument()
    expect(screen.getByText('1 mağaza')).toBeInTheDocument()
  })

  it('says so when the directory is empty', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl({
        '/auth/refresh': anonymous,
        '/categories': () => jsonResponse([]),
        '/stores': () => jsonResponse({ items: [], page: 1, pageSize: 24, total: 0, totalPages: 0 }),
      }),
    )

    renderWithProviders(<StoresPage />, { route: '/magazalar' })

    expect(await screen.findByText('Uyğun mağaza tapılmadı.')).toBeInTheDocument()
  })
})
