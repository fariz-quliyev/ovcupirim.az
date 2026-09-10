import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { ListingCard } from '@/features/listings/types'

import { FavoritesPage } from './FavoritesPage'

const saved: ListingCard = {
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
  hasDelivery: false,
  condition: 'Used',
  sellerType: 'Individual',
  requiresAgeConfirmation: false,
  isFavorited: true,
  publishedAt: '2026-09-02T10:00:00+00:00',
}

describe('FavoritesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('lists what the visitor saved', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/me/favorites': () => jsonResponse({ items: [saved], page: 1, pageSize: 24, total: 1, totalPages: 1 }),
    }))

    renderWithProviders(<FavoritesPage />)

    expect(await screen.findByText('Ov bel çantası 30L')).toBeInTheDocument()
  })

  it('removes by the public listing number', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/me/favorites/2001': () => new Response(null, { status: 204 }),
      '/me/favorites': () => jsonResponse({ items: [saved], page: 1, pageSize: 24, total: 1, totalPages: 1 }),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<FavoritesPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Seçilmişlərdən çıxar' }))

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).endsWith('/me/favorites/2001') &&
            (init as RequestInit | undefined)?.method === 'DELETE',
        ),
      ).toBe(true),
    )
  })

  it('says so when nothing is saved yet', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/me/favorites': () => jsonResponse({ items: [], page: 1, pageSize: 24, total: 0, totalPages: 0 }),
    }))

    renderWithProviders(<FavoritesPage />)

    expect(await screen.findByText('Hələ heç nə seçməmisiniz.')).toBeInTheDocument()
  })
})
