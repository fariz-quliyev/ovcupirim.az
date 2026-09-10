import { useState } from 'react'
import { Link, useNavigate } from 'react-router'

import { ApiError } from '@/api/client'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import * as authApi from '@/features/auth/api'
import { useAuth } from '@/features/auth/useAuth'

export function AccountPage() {
  const { user, setUser, signOut } = useAuth()
  const navigate = useNavigate()

  const [fullName, setFullName] = useState(user?.fullName ?? '')
  const [email, setEmail] = useState(user?.email ?? '')
  const [errors, setErrors] = useState<{ fullName?: string; email?: string; general?: string }>({})
  const [savedAt, setSavedAt] = useState<number | null>(null)
  const [isSaving, setIsSaving] = useState(false)

  if (!user) {
    return null
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setErrors({})
    setSavedAt(null)
    setIsSaving(true)

    try {
      const updated = await authApi.updateProfile(fullName, email.trim() === '' ? null : email.trim())
      setUser(updated)
      setSavedAt(Date.now())
    } catch (caught) {
      if (caught instanceof ApiError) {
        const nameError = caught.fieldError('fullName')
        const emailError = caught.fieldError('email')

        setErrors({
          ...(nameError ? { fullName: nameError } : {}),
          ...(emailError ? { email: emailError } : {}),
          ...(nameError || emailError ? {} : { general: caught.message }),
        })
      } else {
        setErrors({ general: 'Şəbəkə xətası. Yenidən cəhd edin.' })
      }
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="py-10">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl">Şəxsi kabinet</h1>
          <p className="mt-1 text-sm text-muted">{user.phoneNumber}</p>
        </div>

        <div className="flex items-center gap-3">
          {user.isPhoneVerified ? <Badge tone="new">Nömrə təsdiqlənib</Badge> : null}
          {user.role !== 'User' ? <Badge tone="store">{user.role}</Badge> : null}

          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              void (async () => {
                await signOut()
                await navigate('/', { replace: true })
              })()
            }}
          >
            Çıxış
          </Button>
        </div>
      </div>

      <nav className="mt-6 flex flex-wrap gap-3 text-sm">
        <Link to="/kabinet/elanlarim" className="text-interactive hover:underline">
          Elanlarım
        </Link>
        <Link to="/kabinet/magazam" className="text-interactive hover:underline">
          Mağazam
        </Link>
        <Link to="/kabinet/bildirisler" className="text-interactive hover:underline">
          Bildirişlər
        </Link>
        <Link to="/kabinet/odenisler" className="text-interactive hover:underline">
          Ödənişlərim
        </Link>
        <Link to="/secilmisler" className="text-interactive hover:underline">
          Seçilmişlər
        </Link>
      </nav>

      <section className="mt-6 max-w-lg rounded-(--radius-card) border border-line bg-surface p-6">
        <h2 className="text-lg">Profil məlumatları</h2>

        <form onSubmit={handleSubmit} className="mt-4 flex flex-col gap-4" noValidate>
          <Input
            label="Ad və soyad"
            autoComplete="name"
            value={fullName}
            onChange={(event) => setFullName(event.target.value)}
            error={errors.fullName}
          />

          <Input
            label="E-mail (istəyə bağlı)"
            type="email"
            autoComplete="email"
            placeholder="ad@example.com"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={errors.email}
          />

          <Input
            label="Mobil nömrə"
            value={user.phoneNumber}
            readOnly
            disabled
            hint="Nömrəni dəyişmək üçün yeni nömrəni SMS ilə təsdiqləmək lazımdır."
            className="bg-canvas text-muted"
          />

          {errors.general ? (
            <p role="alert" className="text-sm text-accent">
              {errors.general}
            </p>
          ) : null}

          {savedAt !== null ? (
            <p role="status" className="text-sm text-interactive">
              Məlumatlar yadda saxlanıldı.
            </p>
          ) : null}

          <div>
            <Button type="submit" disabled={isSaving || fullName.trim().length === 0}>
              {isSaving ? 'Saxlanılır…' : 'Yadda saxla'}
            </Button>
          </div>
        </form>
      </section>
    </div>
  )
}
