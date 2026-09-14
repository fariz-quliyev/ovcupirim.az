import { screen, waitFor } from '@testing-library/react'
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

import { AdminLoginPage } from './AdminLoginPage'

const admin = {
  id: 'e2f3a0d1-0000-4000-8000-000000000001',
  phoneNumber: '+994557654321',
  fullName: 'Administrator',
  email: null,
  role: 'Admin',
  isPhoneVerified: true,
  createdAt: '2026-01-01T00:00:00Z',
}

function page() {
  return (
    <Routes>
      <Route path="/admin/giris" element={<AdminLoginPage />} />
      <Route path="/admin" element={<h1>İdarə paneli işə düşdü</h1>} />
    </Routes>
  )
}

function mockAuth(adminLogin: () => Response) {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/auth/admin/login': adminLogin,
    }),
  )
}

describe('AdminLoginPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('asks for a password and never offers an SMS code', async () => {
    mockAuth(() => jsonResponse({ accessToken: 'token', expiresInSeconds: 900, user: admin }))
    renderWithProviders(page(), { route: '/admin/giris' })

    expect(await screen.findByLabelText('Parol')).toBeInTheDocument()
    expect(screen.getByLabelText('Mobil nömrə')).toBeInTheDocument()

    // The public flow's affordances must not appear here: an Admin account cannot use them.
    expect(screen.queryByRole('button', { name: /kodu göndər/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/SMS ilə təsdiq kodu göndərəcəyik/i)).not.toBeInTheDocument()
  })

  it('signs in and lands on the panel', async () => {
    const user = userEvent.setup()
    mockAuth(() => jsonResponse({ accessToken: 'token', expiresInSeconds: 900, user: admin }))
    renderWithProviders(page(), { route: '/admin/giris' })

    await user.type(await screen.findByLabelText('Mobil nömrə'), '+994557654321')
    await user.type(screen.getByLabelText('Parol'), 'duzgun-parol-2026')
    await user.click(screen.getByRole('button', { name: 'Daxil ol' }))

    expect(await screen.findByRole('heading', { name: 'İdarə paneli işə düşdü' })).toBeInTheDocument()
  })

  it('shows the server message and clears the password after a rejection', async () => {
    const user = userEvent.setup()
    mockAuth(() =>
      problemResponse(401, 'Giriş mümkün olmadı. Məlumatları yoxlayıb yenidən cəhd edin.'),
    )
    renderWithProviders(page(), { route: '/admin/giris' })

    await user.type(await screen.findByLabelText('Mobil nömrə'), '+994557654321')
    await user.type(screen.getByLabelText('Parol'), 'yanlis-parol-2026')
    await user.click(screen.getByRole('button', { name: 'Daxil ol' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Giriş mümkün olmadı.')

    // Cleared, so a second attempt is typed afresh rather than resubmitting the same wrong value.
    await waitFor(() => expect(screen.getByLabelText('Parol')).toHaveValue(''))
  })
})
