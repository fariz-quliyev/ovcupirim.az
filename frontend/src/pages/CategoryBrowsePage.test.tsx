import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
} from '@/features/auth/authTestUtils'
import type { CategoryNode } from '@/features/catalog/types'
import { CategoriesPage } from '@/pages/CategoriesPage'
import { CategoryBrowsePage } from '@/pages/CategoryBrowsePage'

function node(slug: string, nameAz: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: slug.length,
    slug,
    nameAz,
    nameRu: null,
    iconKey: 'baliq',
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
  node('baliqciliq', 'Balıqçılıq', [node('makara', 'Tilov çarxı'), node('misina', 'Tilov sapı')]),
  node('kamp', 'Kamp', []),
]

function page() {
  return (
    <Routes>
      <Route path="/kateqoriyalar" element={<CategoriesPage />} />
      <Route path="/kateqoriyalar/:categorySlug" element={<CategoryBrowsePage />} />
    </Routes>
  )
}

function mockTaxonomy() {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/categories': () => jsonResponse(tree),
    }),
  )
}

describe('CategoryBrowsePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('leads with the whole category, then its subcategories', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/kateqoriyalar/baliqciliq' })

    // The category is a destination in its own right, not just a folder — someone after fishing
    // gear in general must not have to pick a subcategory to get anywhere.
    expect(await screen.findByRole('link', { name: 'Bütün elanlar' })).toHaveAttribute(
      'href',
      '/elanlar/baliqciliq',
    )

    expect(screen.getByRole('link', { name: 'Tilov çarxı' })).toHaveAttribute(
      'href',
      '/elanlar/baliqciliq/makara',
    )
    expect(screen.getByRole('link', { name: 'Tilov sapı' })).toHaveAttribute(
      'href',
      '/elanlar/baliqciliq/misina',
    )
  })

  it('names the category in its own bar and offers a way back', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/kateqoriyalar/baliqciliq' })

    expect(await screen.findAllByText('Balıqçılıq')).not.toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Geri' })).toBeInTheDocument()
  })

  it('is what a category row on the catalogue screen opens', async () => {
    const user = userEvent.setup()
    mockTaxonomy()
    renderWithProviders(page(), { route: '/kateqoriyalar' })

    // Both presentations of the catalogue are in the document — CSS picks one — so the row is
    // identified by where it goes rather than by which list it sits in.
    const rows = await screen.findAllByRole('link', { name: /Balıqçılıq/ })
    const row = rows.find((link) => link.getAttribute('href') === '/kateqoriyalar/baliqciliq')

    expect(row).toBeDefined()
    await user.click(row!)

    expect(await screen.findByRole('link', { name: 'Bütün elanlar' })).toBeInTheDocument()
  })

  it('still offers the category itself when it has no subcategories', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/kateqoriyalar/kamp' })

    const links = await screen.findAllByRole('link')

    expect(links).toHaveLength(1)
    expect(links[0]).toHaveAttribute('href', '/elanlar/kamp')
  })

  it('says so when the slug matches nothing', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/kateqoriyalar/yoxdur' })

    expect(await screen.findByText('Kateqoriya tapılmadı')).toBeInTheDocument()
  })
})
