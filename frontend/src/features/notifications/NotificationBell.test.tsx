import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from '@/features/auth/authTestUtils'

import { NotificationBell } from './NotificationBell'

/**
 * The bell reads useAuth().isAuthenticated, which only becomes true once AuthProvider's own boot
 * refresh resolves — so, unlike NotificationsPage, "signed in" here means mocking a successful
 * /auth/refresh, not just pre-seeding an access token that the boot effect would overwrite anyway.
 */
function mockSignedIn(unreadCount: number) {
  return mockFetchByUrl({
    '/auth/refresh': () => jsonResponse(testAuthResponse),
    '/me/notifications/unread-count': () => jsonResponse({ count: unreadCount }),
  })
}

describe('NotificationBell', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders nothing while signed out', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problemResponse(401, 'Sessiya tapılmadı.')))

    const { container } = renderWithProviders(<NotificationBell />)

    // The boot refresh has to settle before "signed out" is knowable.
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('shows the unread count once signed in', async () => {
    vi.stubGlobal('fetch', mockSignedIn(3))

    renderWithProviders(<NotificationBell />)

    expect(await screen.findByText('3')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Bildirişlər, 3 oxunmamış' })).toBeInTheDocument()
  })

  it('caps the visible badge at 9+', async () => {
    vi.stubGlobal('fetch', mockSignedIn(42))

    renderWithProviders(<NotificationBell />)

    expect(await screen.findByText('9+')).toBeInTheDocument()
  })

  it('shows no badge when everything is read', async () => {
    vi.stubGlobal('fetch', mockSignedIn(0))

    renderWithProviders(<NotificationBell />)

    await screen.findByRole('link', { name: 'Bildirişlər' })
    expect(screen.queryByText('0')).not.toBeInTheDocument()
  })
})
