import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
} from '@/features/auth/authTestUtils'
import type { CategoryNode, Region } from '@/features/catalog/types'
import type { ListingCard } from '@/features/listings/types'
import { HomePage } from '@/pages/HomePage'

function node(slug: string, nameAz: string, iconKey: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: slug.length,
    slug,
    nameAz,
    nameRu: null,
    iconKey,
    imageKey: null,
    sortOrder: 10,
    listingCount: 0,
    restrictionStatus: 'Unrestricted',
    requiresAgeConfirmation: false,
    isSelectable: true,
    children,
  }
}

const tree: CategoryNode[] = [
  node('ovculuq', 'Ovçuluq', 'ov', [node('ov-cantalari', 'Ov çantaları', 'canta')]),
  node('baliqciliq', 'Balıqçılıq', 'baliq', [node('tilovlar', 'Tilovlar', 'baliq')]),
  node('kamp', 'Kamp', 'kamp', [node('cadir', 'Çadır', 'kamp')]),
  node('outdoor-geyim', 'Outdoor geyim', 'geyim', [node('salvar', 'Şalvar', 'geyim')]),
]

const regions: Region[] = [
  { id: 1, slug: 'baki', nameAz: 'Bakı', nameRu: null, listingCount: 4 },
  { id: 2, slug: 'gence', nameAz: 'Gəncə', nameRu: null, listingCount: 0 },
]

const listing: ListingCard = {
  shortId: 2,
  slug: 'ov-cantasi-2',
  path: '/elan/ov-cantasi-2',
  title: 'Ov çantası',
  price: 150,
  currency: 'AZN',
  regionNameAz: 'Bakı',
  categorySlug: 'ov-cantalari',
  categoryNameAz: 'Ov çantaları',
  imageUrl: null,
  hasDelivery: false,
  condition: 'Used',
  sellerType: 'Individual',
  requiresAgeConfirmation: false,
  isFavorited: false,
  publishedAt: '2026-09-14T00:00:00Z',
}

function mockHome(items: ListingCard[], categories: CategoryNode[] = tree) {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/categories': () => jsonResponse(categories),
      '/regions': () => jsonResponse(regions),
      '/listings': () =>
        jsonResponse({
          items,
          page: 1,
          pageSize: 12,
          total: items.length,
          totalIsExact: true,
          totalPages: 1,
          sort: 'newest',
        }),
    }),
  )
}

function page() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
    </Routes>
  )
}

describe('HomePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders every section of the designed homepage', async () => {
    mockHome([listing])
    renderWithProviders(page(), { route: '/' })

    for (const section of [
      'Kateqoriyalar',
      'Premium elanlar',
      'Son elanlar',
    ]) {
      expect(await screen.findByRole('heading', { name: section })).toBeInTheDocument()
    }
  })

  it('drives its content from the taxonomy rather than a hard-coded list', async () => {
    mockHome([listing])
    renderWithProviders(page(), { route: '/' })

    // Every top-level category reaches the page, linking to its own catalogue. A category appears
    // both as a tile and as a "Populyar" pill, so every match should carry the same destination.
    const ovculuq = await screen.findAllByRole('link', { name: 'Ovçuluq' })
    expect(ovculuq.length).toBeGreaterThan(0)
    for (const link of ovculuq) {
      expect(link).toHaveAttribute('href', '/elanlar/ovculuq')
    }
    expect(screen.getAllByRole('link', { name: 'Outdoor geyim' })[0]).toBeInTheDocument()

    // Regions populate the search band's filter.
    expect(screen.getByRole('option', { name: 'Bakı' })).toBeInTheDocument()

    // And the newest listing is in the feed.
    expect(screen.getByText('Ov çantası')).toBeInTheDocument()
  })

  it('shows a category picture once one is set, and a glyph until then', async () => {
    // imageKey is what an administrator edits; the tile falls back to its glyph while it is unset,
    // so adding photography later never leaves a category with an empty square.
    mockHome([listing], [
      { ...tree[0]!, imageKey: 'categories/ovculuq.jpg' },
      tree[1]!,
    ])
    renderWithProviders(page(), { route: '/' })

    // The tile picture is decorative — the category name beside it already says what it is — so it
    // carries an empty alt and is presentational, which means it has no `img` role to query by.
    await screen.findAllByRole('link', { name: 'Ovçuluq' })
    const pictures = document.querySelectorAll('img')

    expect(pictures).toHaveLength(1)
    expect(pictures[0]).toHaveAttribute('src', '/uploads/categories/ovculuq.jpg')

    // The second category has no picture, and its tile is still there.
    expect(screen.getAllByRole('link', { name: 'Balıqçılıq' })[0]).toBeInTheDocument()
  })

  it('keeps the premium and listing sections in place when there is nothing to show', async () => {
    // An empty marketplace still has to read as a marketplace: the designed sections stay and
    // explain themselves rather than disappearing.
    mockHome([])
    renderWithProviders(page(), { route: '/' })

    expect(await screen.findByRole('heading', { name: 'Premium elanlar' })).toBeInTheDocument()
    expect(screen.getByText('Hazırda premium elan yoxdur.')).toBeInTheDocument()

    // The listing feed settles a tick later than the static sections around it.
    expect(await screen.findByText('Hələ dərc olunmuş elan yoxdur.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Elan yerləşdir' })).toBeInTheDocument()
  })

  it('shows no Phase 1 or API diagnostic content', async () => {
    // The page this replaced shipped a build-status panel to visitors; it must not come back.
    mockHome([listing])
    renderWithProviders(page(), { route: '/' })

    await screen.findByRole('heading', { name: 'Son elanlar' })

    expect(screen.queryByText(/Phase 1/)).not.toBeInTheDocument()
    expect(screen.queryByText(/API bağlantısı/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Layihənin bünövrəsi/)).not.toBeInTheDocument()
  })
})
