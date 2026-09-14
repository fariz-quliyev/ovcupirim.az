import { useState } from 'react'
import { useNavigate } from 'react-router'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import * as authApi from '@/features/auth/api'
import { useAuth } from '@/features/auth/useAuth'

/** Matches the server rule in AdminLoginOptions. */
const MINIMUM_LENGTH = 12

export function AdminPasswordPage() {
  const { signOut } = useAuth()
  const navigate = useNavigate()

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [repeated, setRepeated] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const mismatch = repeated.length > 0 && repeated !== newPassword
  const tooShort = newPassword.length > 0 && newPassword.length < MINIMUM_LENGTH

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      await authApi.changePassword(currentPassword, newPassword)

      // The server revoked every session, this one included. Clearing locally and returning to the
      // sign-in screen is the honest reflection of that, rather than letting the next request 401.
      await signOut()
      void navigate('/admin/giris', { replace: true })
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (caught.fieldError('newPassword') ?? caught.message)
          : 'Şəbəkə xətası. Yenidən cəhd edin.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="max-w-md">
      <h1 className="text-2xl">Parolu dəyiş</h1>
      <p className="mt-2 text-sm text-muted">
        Parol dəyişdikdən sonra bütün sessiyalar bağlanır və yenidən daxil olmaq lazım gəlir.
      </p>

      <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-4" noValidate>
        <Input
          label="Cari parol"
          type="password"
          autoComplete="current-password"
          value={currentPassword}
          onChange={(event) => setCurrentPassword(event.target.value)}
        />

        <Input
          label="Yeni parol"
          type="password"
          autoComplete="new-password"
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
          hint={`Ən azı ${MINIMUM_LENGTH} simvol.`}
          error={tooShort ? `Parol ən azı ${MINIMUM_LENGTH} simvol olmalıdır.` : undefined}
        />

        <Input
          label="Yeni parolu təkrarlayın"
          type="password"
          autoComplete="new-password"
          value={repeated}
          onChange={(event) => setRepeated(event.target.value)}
          error={mismatch ? 'Parollar uyğun gəlmir.' : (error ?? undefined)}
        />

        <Button
          type="submit"
          disabled={
            isSubmitting ||
            currentPassword.length === 0 ||
            newPassword.length < MINIMUM_LENGTH ||
            repeated !== newPassword
          }
        >
          {isSubmitting ? 'Yadda saxlanılır…' : 'Parolu dəyiş'}
        </Button>
      </form>
    </div>
  )
}
