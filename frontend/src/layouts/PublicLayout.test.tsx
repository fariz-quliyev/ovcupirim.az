import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
} from '@/features/auth/authTestUtils'

import { PublicLayout } from './PublicLayout'

/** Renders the current URL, so a navigation can be asserted on rather than inferred. */
function Where() {
  const location = useLocation()

  return <output>{location.pathname + location.search}</output>
}

function page() {
  return (
    <Routes>
      <Route element={<PublicLayout />}>
        <Route path="/" element={<Where />} />
        <Route path="/axtaris" element={<Where />} />
        <Route path="/elanlar" element={<Where />} />
      </Route>
    </Routes>
  )
}

function mockAnonymous() {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    }),
  )
}

describe('PublicLayout header', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('carries the search field, so it is on every page rather than the homepage alone', async () => {
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    expect(await screen.findByRole('search')).toBeInTheDocument()
    expect(screen.getByLabelText('Avadanlıq və ya marka axtarışı')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Axtar' })).toBeInTheDocument()
  })

  it('searches what was typed', async () => {
    const user = userEvent.setup()
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await user.type(await screen.findByLabelText('Avadanlıq və ya marka axtarışı'), 'tilov çarxı')
    await user.click(screen.getByRole('button', { name: 'Axtar' }))

    expect(screen.getByRole('status')).toHaveTextContent('/axtaris?q=tilov%20%C3%A7arx%C4%B1')
  })

  it('sends an empty search to the full catalogue rather than to an empty result page', async () => {
    const user = userEvent.setup()
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await user.click(await screen.findByRole('button', { name: 'Axtar' }))

    expect(screen.getByRole('status')).toHaveTextContent('/elanlar')
  })

  it('keeps the catalogue and the primary actions reachable from the bar', async () => {
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await screen.findByRole('search')

    // Scoped to the header: the phone's bottom bar carries some of the same destinations, and an
    // unscoped query would pass on those instead of on the bar this test is about.
    const header = within(screen.getByRole('banner'))

    for (const [label, href] of [
      ['Kataloq', '/kateqoriyalar'],
      ['Seçilmişlər', '/secilmisler'],
      ['Giriş', '/giris'],
      ['+ Yeni elan', '/yeni-elan'],
    ] as const) {
      expect(header.getByRole('link', { name: label })).toHaveAttribute('href', href)
    }
  })

  it('still reaches the sections the old link row carried, from the footer', async () => {
    // The four header links were traded for the search field. None of them became unreachable.
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await screen.findByRole('search')

    for (const [label, href] of [
      ['Kateqoriyalar', '/kateqoriyalar'],
      ['Elanlar', '/elanlar'],
      ['Mağazalar', '/magazalar'],
      ['Bələdçi', '/beledci'],
    ] as const) {
      expect(screen.getAllByRole('link', { name: label })[0]).toHaveAttribute('href', href)
    }
  })
})
