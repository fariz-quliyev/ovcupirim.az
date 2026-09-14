import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
} from '@/features/auth/authTestUtils'
import type { CategoryNode } from '@/features/catalog/types'

import { PublicLayout } from './PublicLayout'

function node(slug: string, nameAz: string, children: CategoryNode[] = []): CategoryNode {
  return {
    id: slug.length,
    slug,
    nameAz,
    nameRu: null,
    iconKey: 'ov',
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
  node('ovculuq', 'Ovçuluq', [node('ov-cantalari', 'Ov çantaları'), node('ov-geyimleri', 'Ov geyimləri')]),
  node('baliqciliq', 'Balıqçılıq', [node('makara', 'Tilov çarxı'), node('misina', 'Tilov sapı')]),
  node('kamp', 'Kamp', []),
]

function Where() {
  return <output>{useLocation().pathname}</output>
}

function page() {
  return (
    <Routes>
      <Route element={<PublicLayout />}>
        <Route path="/" element={<Where />} />
        <Route path="/elanlar/:slug" element={<Where />} />
      </Route>
    </Routes>
  )
}

function mockTaxonomy(categories: CategoryNode[] = tree) {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/categories': () => jsonResponse(categories),
    }),
  )
}

async function openMenu() {
  const user = userEvent.setup()
  await user.click(await screen.findByRole('button', { name: 'Kataloq' }))

  return user
}

describe('CatalogueMenu', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('stays shut until the catalogue button is pressed', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/' })

    const button = await screen.findByRole('button', { name: 'Kataloq' })
    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('navigation', { name: 'Kateqoriyalar' })).not.toBeInTheDocument()

    await openMenu()

    expect(await screen.findByRole('navigation', { name: 'Kateqoriyalar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Kataloq' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('opens with nothing selected', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/' })
    await openMenu()

    await screen.findByRole('navigation', { name: 'Kateqoriyalar' })

    // No category is chosen for the reader, so no subcategories are shown yet.
    expect(
      screen.queryByRole('navigation', { name: /alt kateqoriyaları/ }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ov çantaları' })).not.toBeInTheDocument()
    expect(
      screen.getByText('Alt bölmələri görmək üçün kateqoriyanın üzərinə gəlin.'),
    ).toBeInTheDocument()
  })

  it('fills the second column from whichever category is pointed at, and swaps on the next', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/' })
    const user = await openMenu()

    const primary = within(await screen.findByRole('navigation', { name: 'Kateqoriyalar' }))

    await user.hover(primary.getByRole('link', { name: 'Ovçuluq' }))
    expect(
      within(await screen.findByRole('navigation', { name: 'Ovçuluq alt kateqoriyaları' }))
        .getByRole('link', { name: 'Ov çantaları' }),
    ).toHaveAttribute('href', '/elanlar/ov-cantalari')

    await user.hover(primary.getByRole('link', { name: 'Balıqçılıq' }))
    const subcategories = await screen.findByRole('navigation', {
      name: 'Balıqçılıq alt kateqoriyaları',
    })

    expect(within(subcategories).getByRole('link', { name: 'Tilov çarxı' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ov çantaları' })).not.toBeInTheDocument()
  })

  it('follows keyboard focus as well as the pointer', async () => {
    // Tabbing through the left column has to move the right column with it, or a keyboard reader
    // walks a list whose contents never match what is highlighted.
    mockTaxonomy()
    renderWithProviders(page(), { route: '/' })
    const user = await openMenu()

    const primary = within(await screen.findByRole('navigation', { name: 'Kateqoriyalar' }))
    primary.getByRole('link', { name: 'Balıqçılıq' }).focus()

    expect(
      await screen.findByRole('navigation', { name: 'Balıqçılıq alt kateqoriyaları' }),
    ).toBeInTheDocument()

    // And Escape closes it.
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('navigation', { name: 'Kateqoriyalar' })).not.toBeInTheDocument()
  })

  it('navigates and closes when a subcategory is chosen', async () => {
    mockTaxonomy()
    renderWithProviders(page(), { route: '/' })
    const user = await openMenu()

    const primary = within(await screen.findByRole('navigation', { name: 'Kateqoriyalar' }))
    await user.hover(primary.getByRole('link', { name: 'Ovçuluq' }))

    await user.click(await screen.findByRole('link', { name: 'Ov geyimləri' }))

    expect(screen.getByRole('status')).toHaveTextContent('/elanlar/ov-geyimleri')
    expect(screen.queryByRole('navigation', { name: 'Kateqoriyalar' })).not.toBeInTheDocument()
  })

  it('says so plainly when a category has no subcategories yet', async () => {
    mockTaxonomy([tree[2]!])
    renderWithProviders(page(), { route: '/' })
    const user = await openMenu()

    const primary = within(await screen.findByRole('navigation', { name: 'Kateqoriyalar' }))
    await user.hover(primary.getByRole('link', { name: 'Kamp' }))

    expect(await screen.findByText('Bu bölmədə hələ alt kateqoriya yoxdur.')).toBeInTheDocument()

    // The category itself is still reachable — an empty branch must not be a dead end.
    expect(screen.getByRole('link', { name: /Kamp — hamısı/ })).toHaveAttribute(
      'href',
      '/elanlar/kamp',
    )
  })
})
