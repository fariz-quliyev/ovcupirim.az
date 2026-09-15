import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
  node('baliqciliq', 'Balıqçılıq', [node('tilovlar', 'Tilovlar'), node('makara', 'Tilov çarxı')]),
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
      <Route path="/" element={<h1>Ana səhifə</h1>} />
    </Routes>
  )
}

describe('CategoriesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  // The page renders both presentations and lets CSS pick one, so every name is in the document
  // twice. Only one is ever visible — and only one is in the accessibility tree, since the hidden
  // half is display:none — but jsdom applies no stylesheet, so these queries see both.
  it('renders every category with its subcategories', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    expect((await screen.findAllByText('Balıqçılıq')).length).toBeGreaterThan(0)
    expect(screen.getByText('Tilovlar')).toBeInTheDocument()
    expect(screen.getByText('Tilov çarxı')).toBeInTheDocument()
    expect(screen.getAllByText('Bıçaq və alət').length).toBeGreaterThan(0)
  })

  it('carries its own title bar on the phone, with a way out', async () => {
    // The site header is hidden on this route at phone width, so the screen has to provide both
    // the title and the way back itself.
    const user = userEvent.setup()
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    expect(await screen.findByText('Kataloq')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Bağla' }))

    // Opened cold, with nothing behind it in the site's history, closing goes home rather than
    // stepping out of the site altogether.
    expect(screen.getByRole('heading', { name: 'Ana səhifə' })).toBeInTheDocument()
  })

  it('summarises a category on the phone row', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    // The narrow row has no space for a link per subcategory, so it names them on one line.
    expect(await screen.findByText('Tilovlar, Tilov çarxı')).toBeInTheDocument()
  })

  it('marks a category awaiting classification without calling it restricted', async () => {
    mockCategories(() => jsonResponse(tree))
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    const badges = await screen.findAllByTitle('Bu kateqoriya üçün təsnifat gözlənilir')

    expect(badges[0]).toHaveTextContent('18+')
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
