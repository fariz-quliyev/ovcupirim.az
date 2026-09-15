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
    expect(screen.getByLabelText('Nə axtarırsınız?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Axtar' })).toBeInTheDocument()
  })

  it('searches what was typed', async () => {
    const user = userEvent.setup()
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await user.type(await screen.findByLabelText('Nə axtarırsınız?'), 'tilov çarxı')
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
      ['Giriş', '/giris'],
      ['+ Yeni elan', '/yeni-elan'],
    ] as const) {
      expect(header.getByRole('link', { name: label })).toHaveAttribute('href', href)
    }

    // Favourites sits in the phone bar's left slot and in the desktop cluster; both are in the
    // document and CSS shows one, so this asserts on all of them rather than on a single match.
    const favourites = header.getAllByRole('link', { name: 'Seçilmişlər' })
    expect(favourites.length).toBeGreaterThan(0)
    for (const link of favourites) {
      expect(link).toHaveAttribute('href', '/secilmisler')
    }

    // Kataloq opens the panel in place, so it is a button rather than a link.
    expect(header.getByRole('button', { name: 'Kataloq' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('keeps the site pages and sign-in behind the phone menu', async () => {
    // The phone bar has room for three things, and none of them is a link row. Everything the
    // footer lists lives behind the menu button instead, sign-in included — the bar's right slot
    // is the posting action now.
    const user = userEvent.setup()
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    const button = await screen.findByRole('button', { name: 'Menyu' })
    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('navigation', { name: 'Sayt menyusu' })).not.toBeInTheDocument()

    await user.click(button)

    const menu = within(await screen.findByRole('navigation', { name: 'Sayt menyusu' }))
    expect(menu.getByRole('link', { name: 'Mağazalar' })).toHaveAttribute('href', '/magazalar')
    expect(menu.getByRole('link', { name: 'Yardım' })).toHaveAttribute('href', '/yardim')
    expect(menu.getByRole('link', { name: 'Giriş' })).toHaveAttribute('href', '/giris')

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('navigation', { name: 'Sayt menyusu' })).not.toBeInTheDocument()
  })

  it('closes the phone menu on the way to wherever it was pointed', async () => {
    const user = userEvent.setup()
    mockAnonymous()
    renderWithProviders(page(), { route: '/' })

    await user.click(await screen.findByRole('button', { name: 'Menyu' }))

    const menu = within(screen.getByRole('navigation', { name: 'Sayt menyusu' }))
    await user.click(menu.getByRole('link', { name: 'Elanlar' }))

    expect(screen.getByRole('status')).toHaveTextContent('/elanlar')
    expect(screen.queryByRole('navigation', { name: 'Sayt menyusu' })).not.toBeInTheDocument()
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
