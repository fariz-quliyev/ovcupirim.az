import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from '@/features/auth/authTestUtils'
import type { StoreOwner } from '@/features/stores/types'

import { MyStorePage } from './MyStorePage'

const mine: StoreOwner = {
  id: '22222222-2222-2222-2222-222222222222',
  slug: 'ovcu-dunyasi',
  name: 'Ovçu Dünyası',
  description: 'Ov və kamp avadanlıqları.',
  address: 'Bakı ş.',
  phone: '+994501234567',
  status: 'Active',
  isVerified: false,
  isPublic: true,
  logoUrl: null,
  bannerUrl: null,
  listingCount: 7,
  followerCount: 3,
  createdAt: '2026-01-15T10:00:00+00:00',
}

function signedIn(routes: Record<string, () => Response>) {
  setAccessToken(testAuthResponse.accessToken)

  return mockFetchByUrl({
    '/auth/refresh': () => jsonResponse(testAuthResponse),
    '/auth/me': () => jsonResponse(testAuthResponse.user),
    ...routes,
  })
}

describe('MyStorePage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('offers the application form to a seller with no storefront', async () => {
    vi.stubGlobal(
      'fetch',
      signedIn({ '/me/store': () => problemResponse(404, 'Mağazanız yoxdur.') }),
    )

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    expect(await screen.findByRole('heading', { name: 'Mağaza aç' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Müraciət göndər' })).toBeInTheDocument()
  })

  it('applies with the four fields and nothing else', async () => {
    const fetchMock = signedIn({
      '/me/store': () =>
        problemResponse(404, 'Mağazanız yoxdur.'),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    await userEvent.type(await screen.findByLabelText('Mağazanın adı *'), 'Yeni Mağaza')
    await userEvent.click(screen.getByRole('button', { name: 'Müraciət göndər' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith('/me/store') && (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({
        name: 'Yeni Mağaza',
        description: null,
        address: null,
        phone: null,
      })
    })
  })

  it('tells a pending applicant that the storefront is not live yet', async () => {
    vi.stubGlobal(
      'fetch',
      signedIn({
        '/me/store': () =>
          jsonResponse({ ...mine, status: 'PendingVerification', isPublic: false }),
      }),
    )

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    expect(await screen.findByText('Yoxlanılır')).toBeInTheDocument()
    expect(screen.getByText(/Təsdiqlənənə qədər mağaza saytda görünmür/)).toBeInTheDocument()
  })

  it('tells a suspended owner that the listings stay up', async () => {
    vi.stubGlobal(
      'fetch',
      signedIn({
        '/me/store': () => jsonResponse({ ...mine, status: 'Suspended', isPublic: false }),
      }),
    )

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    expect(await screen.findByText('Dayandırılıb')).toBeInTheDocument()
    expect(screen.getByText(/Elanlarınız isə öz qaydası ilə saytda qalır/)).toBeInTheDocument()
  })

  it('shows the counters and says the address cannot move', async () => {
    vi.stubGlobal('fetch', signedIn({ '/me/store': () => jsonResponse(mine) }))

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    expect(await screen.findByText('7')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText(/Ünvan dəyişmir: ovcupirim.az\/magaza\/ovcu-dunyasi/)).toBeInTheDocument()
  })

  it('saves an edit with a PUT', async () => {
    const fetchMock = signedIn({ '/me/store': () => jsonResponse(mine) })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    const name = await screen.findByLabelText('Mağazanın adı *')
    await userEvent.clear(name)
    await userEvent.type(name, 'Yeni Ad')
    await userEvent.click(screen.getByRole('button', { name: 'Yadda saxla' }))

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(
          ([url, init]) =>
            String(url).endsWith('/me/store') && (init as RequestInit | undefined)?.method === 'PUT',
        ),
      ).toBe(true),
    )
  })

  it('holds the name field to the length the column actually allows', async () => {
    vi.stubGlobal('fetch', signedIn({ '/me/store': () => jsonResponse(mine) }))

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    // 100, not 120: a longer name used to pass the form and fail on the server.
    expect(await screen.findByLabelText('Mağazanın adı *')).toHaveAttribute('maxLength', '100')
  })

  it('lands a server field error on the field it belongs to', async () => {
    setAccessToken(testAuthResponse.accessToken)

    // The seller has no store yet (GET 404), and the application they submit is refused (POST 400),
    // so this stub has to route on the method as well as the path.
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input)

        if (url.includes('/auth/refresh')) return Promise.resolve(jsonResponse(testAuthResponse))
        if (url.includes('/auth/me')) return Promise.resolve(jsonResponse(testAuthResponse.user))

        if (url.includes('/me/store') && init?.method === 'POST') {
          return Promise.resolve(
            problemResponse(400, 'Göndərilən məlumatlar düzgün deyil.', {
              name: ['Ad 100 simvoldan uzun ola bilməz.'],
            }),
          )
        }

        return Promise.resolve(problemResponse(404, 'Mağazanız yoxdur.'))
      }),
    )

    renderWithProviders(<MyStorePage />, { route: '/kabinet/magazam' })

    await userEvent.type(await screen.findByLabelText('Mağazanın adı *'), 'Yeni Mağaza')
    await userEvent.click(screen.getByRole('button', { name: 'Müraciət göndər' }))

    expect(await screen.findByText('Ad 100 simvoldan uzun ola bilməz.')).toBeInTheDocument()
  })
})
