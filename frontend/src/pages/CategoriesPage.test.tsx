import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router'

import { setAccessToken } from '@/api/authToken'
import {
  deferredResponse,
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
} from '@/features/auth/authTestUtils'
import type { CategoryNode } from '@/features/catalog/types'
import { CategoriesPage } from '@/pages/CategoriesPage'

function node(slug: string, nameAz: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: slug.length,
    slug,
    nameAz,
    nameRu: null,
    iconKey: null,
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
  node('baliqciliq', 'Balıqçılıq', [node('tilovlar', 'Tilovlar'), node('makara', 'Makara')]),
  {
    ...node('bicaq-ve-alet', 'Bıçaq və alət', [node('ov-bicagi', 'Ov bıçağı')]),
    restrictionStatus: 'Unclassified',
    requiresAgeConfirmation: true,
  },
]

/** Auth boot refresh and the categories query fire concurrently, so stub by URL. */
function mockCategories(response: () => Response) {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/categories': response,
    }),
  )
}

function page() {
  return (
    <Routes>
      <Route path="/kateqoriyalar" element={<CategoriesPage />} />
    </Routes>
  )
}

describe('CategoriesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders every category with its subcategories', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    expect(await screen.findByText('Balıqçılıq')).toBeInTheDocument()
    expect(screen.getByText('Tilovlar')).toBeInTheDocument()
    expect(screen.getByText('Makara')).toBeInTheDocument()
    expect(screen.getByText('Bıçaq və alət')).toBeInTheDocument()
  })

  it('marks a category awaiting classification without calling it restricted', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    const badge = await screen.findByTitle('Bu kateqoriya üçün təsnifat gözlənilir')

    expect(badge).toHaveTextContent('18+')
    expect(screen.queryByText(/qadağan/i)).not.toBeInTheDocument()
  })

  it('links a subcategory to its listing route', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    const link = await screen.findByRole('link', { name: 'Tilovlar' })

    expect(link).toHaveAttribute('href', '/elanlar/baliqciliq/tilovlar')
  })

  it('shows a busy state while loading', async () => {
    const pending = deferredResponse()
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(pending.promise))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    expect(screen.getByRole('heading', { name: 'Kateqoriyalar' })).toBeInTheDocument()
    expect(screen.queryByText('Balıqçılıq')).not.toBeInTheDocument()

    // Released before the test ends: this stub also answers the boot refresh, and the client shares
    // one in-flight refresh promise, so abandoning it would wedge the tests that follow.
    pending.release(problemResponse(401, 'Sessiya tapılmadı.'))
    await waitFor(() => expect(screen.queryByText('Balıqçılıq')).not.toBeInTheDocument())
  })

  it('offers a retry when the request fails', async () => {
    mockCategories(() => problemResponse(500, 'Server xətası.'))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Yenidən cəhd et' })).toBeInTheDocument()
  })

  it('shows an empty state when the taxonomy is not seeded', async () => {
    mockCategories(() => jsonResponse([]))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    expect(await screen.findByText('Kateqoriya yoxdur')).toBeInTheDocument()
  })
})
