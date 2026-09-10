import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router'

import { getAccessToken, setAccessToken } from '@/api/authToken'
import {
  fetchCall,
  jsonResponse,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from '@/features/auth/authTestUtils'
import { LoginPage } from '@/pages/LoginPage'

const otpSent = { message: 'Təsdiq kodu göndərildi.', resendAfterSeconds: 60, codeLength: 6 }

function loginRoutes() {
  return (
    <Routes>
      <Route path="/giris" element={<LoginPage />} />
      <Route path="/kabinet" element={<p>şəxsi kabinet</p>} />
    </Routes>
  )
}

/** The provider's boot refresh is always the first call; every test starts signed out. */
function mockFetch(...responses: Response[]) {
  const fetchMock = vi.fn().mockResolvedValueOnce(problemResponse(401, 'Sessiya tapılmadı.'))
  for (const response of responses) {
    fetchMock.mockResolvedValueOnce(response)
  }
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('LoginPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('asks for the phone number first', async () => {
    mockFetch()
    renderWithProviders(loginRoutes(), { route: '/giris' })

    expect(await screen.findByLabelText('Mobil nömrə')).toBeInTheDocument()
    expect(screen.queryByLabelText('Təsdiq kodu')).not.toBeInTheDocument()
  })

  it('moves to the code step after requesting a code', async () => {
    const fetchMock = mockFetch(jsonResponse(otpSent))
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '0501234567')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    expect(await screen.findByLabelText('Təsdiq kodu')).toBeInTheDocument()

    const loginCall = fetchCall(fetchMock, 1)
    expect(loginCall.url).toContain('/auth/login')
    expect(loginCall.body).toEqual({ phoneNumber: '0501234567' })
  })

  it('signs the user in and redirects once the code is accepted', async () => {
    const fetchMock = mockFetch(jsonResponse(otpSent), jsonResponse(testAuthResponse))
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '0501234567')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    await userEvent.type(await screen.findByLabelText('Təsdiq kodu'), '123456')
    await userEvent.click(screen.getByRole('button', { name: 'Təsdiqlə' }))

    await waitFor(() => expect(screen.getByText('şəxsi kabinet')).toBeInTheDocument())
    expect(getAccessToken()).toBe('test-access-token')

    expect(fetchCall(fetchMock, 2).body).toEqual({
      phoneNumber: '0501234567',
      code: '123456',
      purpose: 1,
    })
  })

  it('shows the field error when the phone number is rejected', async () => {
    mockFetch(
      problemResponse(400, 'Göndərilən məlumatlar düzgün deyil.', {
        phoneNumber: ['Telefon nömrəsi düzgün deyil. Nümunə: +994501234567'],
      }),
    )
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '123')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Telefon nömrəsi düzgün deyil')
    // Still on the first step — a rejected number never advances the flow.
    expect(screen.queryByLabelText('Təsdiq kodu')).not.toBeInTheDocument()
  })

  it('shows the server message when the code is wrong and stays on the code step', async () => {
    mockFetch(jsonResponse(otpSent), problemResponse(400, 'Kod yanlışdır və ya vaxtı bitib.'))
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '0501234567')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    await userEvent.type(await screen.findByLabelText('Təsdiq kodu'), '000000')
    await userEvent.click(screen.getByRole('button', { name: 'Təsdiqlə' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Kod yanlışdır və ya vaxtı bitib.')
    expect(screen.getByLabelText('Təsdiq kodu')).toBeInTheDocument()
    expect(getAccessToken()).toBeNull()
  })

  it('disables resend until the cooldown has elapsed', async () => {
    mockFetch(jsonResponse(otpSent))
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '0501234567')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    const resend = await screen.findByRole('button', { name: /Yenidən göndər/ })
    expect(resend).toBeDisabled()
  })

  it('lets the user go back and correct the number', async () => {
    mockFetch(jsonResponse(otpSent))
    renderWithProviders(loginRoutes(), { route: '/giris' })

    await userEvent.type(await screen.findByLabelText('Mobil nömrə'), '0501234567')
    await userEvent.click(screen.getByRole('button', { name: 'Kodu göndər' }))

    await userEvent.click(await screen.findByRole('button', { name: 'Nömrəni dəyiş' }))

    expect(screen.getByLabelText('Mobil nömrə')).toBeInTheDocument()
  })
})
