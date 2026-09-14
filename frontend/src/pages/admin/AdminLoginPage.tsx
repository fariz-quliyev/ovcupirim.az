import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import * as authApi from '@/features/auth/api'
import { useAuth } from '@/features/auth/useAuth'

/**
 * The administrator's sign-in screen, deliberately separate from the public one.
 *
 * There is no code step and no "send me an SMS" fallback: an Admin account is outside the SMS flow
 * server-side, so the password is the whole of the credential. A forgotten password is recovered
 * from the server console, not from here.
 */
export function AdminLoginPage() {
  const { signIn, isAuthenticated, isLoading, user } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [phoneNumber, setPhoneNumber] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/admin'

  // Already signed in as an operator: this screen has nothing to ask.
  if (!isLoading && isAuthenticated && (user?.role === 'Admin' || user?.role === 'Moderator')) {
    return <Navigate to={redirectTo} replace />
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      const auth = await authApi.adminLogin(phoneNumber, password)
      signIn(auth)
      void navigate(redirectTo, { replace: true })
    } catch (caught) {
      // The server answers one message for every rejection — wrong number, wrong password, locked,
      // not an administrator. Showing it verbatim keeps the client from inventing a distinction the
      // server deliberately refuses to make.
      setError(
        caught instanceof ApiError
          ? caught.message
          : 'Şəbəkə xətası. Yenidən cəhd edin.',
      )
      setPassword('')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="mx-auto w-full max-w-md py-12">
      <div className="rounded-(--radius-card) border border-line bg-surface p-6 sm:p-8">
        <h1 className="text-2xl">İdarə paneli</h1>
        <p className="mt-2 text-sm text-muted">
          Administrator girişi. Bu hesab SMS ilə deyil, parolla daxil olur.
        </p>

        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-4" noValidate>
          <Input
            label="Mobil nömrə"
            type="tel"
            inputMode="tel"
            autoComplete="username"
            placeholder="+994 50 123 45 67"
            value={phoneNumber}
            onChange={(event) => setPhoneNumber(event.target.value)}
            autoFocus
          />

          <Input
            label="Parol"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            error={error ?? undefined}
          />

          <Button
            type="submit"
            disabled={isSubmitting || phoneNumber.trim().length === 0 || password.length === 0}
          >
            {isSubmitting ? 'Yoxlanılır…' : 'Daxil ol'}
          </Button>
        </form>

        <p className="mt-6 border-t border-line pt-4 text-sm text-muted">
          Ardıcıl beş yanlış cəhddən sonra hesab 15 dəqiqəlik bağlanır.
        </p>
      </div>
    </div>
  )
}
