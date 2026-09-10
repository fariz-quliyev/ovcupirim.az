import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router'

import { setAccessToken } from '@/api/authToken'

import { ProtectedRoute } from './ProtectedRoute'
import {
  deferredResponse,
  jsonResponse,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from './authTestUtils'

function routes() {
  return (
    <Routes>
      <Route path="/giris" element={<p>giriş səhifəsi</p>} />
      <Route path="/" element={<p>ana səhifə</p>} />
      <Route element={<ProtectedRoute />}>
        <Route path="/kabinet" element={<p>şəxsi kabinet</p>} />
      </Route>
      <Route element={<ProtectedRoute roles={['Admin']} />}>
        <Route path="/admin" element={<p>admin paneli</p>} />
      </Route>
    </Routes>
  )
}

describe('ProtectedRoute', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('redirects an anonymous visitor to the login page', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problemResponse(401, 'Sessiya tapılmadı.')))

    renderWithProviders(routes(), { route: '/kabinet' })

    await waitFor(() => expect(screen.getByText('giriş səhifəsi')).toBeInTheDocument())
    expect(screen.queryByText('şəxsi kabinet')).not.toBeInTheDocument()
  })

  it('renders the guarded page for a signed-in user', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(testAuthResponse)))

    renderWithProviders(routes(), { route: '/kabinet' })

    await waitFor(() => expect(screen.getByText('şəxsi kabinet')).toBeInTheDocument())
  })

  it('does not redirect while the session is still being restored', async () => {
    const restore = deferredResponse()
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(restore.promise))

    renderWithProviders(routes(), { route: '/kabinet' })

    // Neither the guarded page nor a premature redirect — just the loading placeholder.
    expect(screen.queryByText('şəxsi kabinet')).not.toBeInTheDocument()
    expect(screen.queryByText('giriş səhifəsi')).not.toBeInTheDocument()

    // Let the restore finish, so the shared refresh promise is not left occupied.
    restore.release(jsonResponse(testAuthResponse))
    await waitFor(() => expect(screen.getByText('şəxsi kabinet')).toBeInTheDocument())
  })

  it('keeps a non-admin out of a role-gated route', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(testAuthResponse)))

    renderWithProviders(routes(), { route: '/admin' })

    await waitFor(() => expect(screen.getByText('ana səhifə')).toBeInTheDocument())
    expect(screen.queryByText('admin paneli')).not.toBeInTheDocument()
  })

  it('lets an admin through a role-gated route', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ ...testAuthResponse, user: { ...testAuthResponse.user, role: 'Admin' } }),
      ),
    )

    renderWithProviders(routes(), { route: '/admin' })

    await waitFor(() => expect(screen.getByText('admin paneli')).toBeInTheDocument())
  })
})
