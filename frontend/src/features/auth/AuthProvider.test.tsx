import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { getAccessToken, setAccessToken } from '@/api/authToken'

import {
  fetchCall,
  jsonResponse,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from './authTestUtils'
import { useAuth } from './useAuth'

function AuthProbe() {
  const { user, isLoading, isAuthenticated, signOut } = useAuth()

  if (isLoading) {
    return <p>yüklənir</p>
  }

  return (
    <div>
      <p data-testid="state">{isAuthenticated ? `signed-in:${user?.fullName}` : 'signed-out'}</p>
      <button type="button" onClick={() => void signOut()}>
        Çıxış
      </button>
    </div>
  )
}

describe('AuthProvider', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('restores the session from the refresh cookie on boot', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(testAuthResponse)))

    renderWithProviders(<AuthProbe />)

    await waitFor(() => {
      expect(screen.getByTestId('state')).toHaveTextContent('signed-in:Test İstifadəçi')
    })
    expect(getAccessToken()).toBe('test-access-token')
  })

  it('stays signed out when there is no valid refresh cookie', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problemResponse(401, 'Sessiya tapılmadı.')))

    renderWithProviders(<AuthProbe />)

    await waitFor(() => {
      expect(screen.getByTestId('state')).toHaveTextContent('signed-out')
    })
    expect(getAccessToken()).toBeNull()
  })

  it('shows the loading state until the boot refresh settles', async () => {
    let release: ((value: Response) => void) | undefined
    const pending = new Promise<Response>((resolve) => {
      release = resolve
    })
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(pending))

    renderWithProviders(<AuthProbe />)

    expect(screen.getByText('yüklənir')).toBeInTheDocument()

    release!(problemResponse(401, 'Sessiya tapılmadı.'))
    await waitFor(() => expect(screen.getByTestId('state')).toBeInTheDocument())
  })

  it('clears the token and user on sign-out', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(testAuthResponse))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))

    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AuthProbe />)
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'))

    await userEvent.click(screen.getByRole('button', { name: 'Çıxış' }))

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'))
    expect(getAccessToken()).toBeNull()
    expect(fetchCall(fetchMock, 1).url).toContain('/auth/logout')
  })

  it('signs out locally even if the logout call fails', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(testAuthResponse))
      .mockRejectedValueOnce(new Error('network down'))

    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AuthProbe />)
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'))

    await userEvent.click(screen.getByRole('button', { name: 'Çıxış' }))

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'))
    expect(getAccessToken()).toBeNull()
  })
})
