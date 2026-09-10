import { useState } from 'react'
import { NavLink, useLocation, useNavigate } from 'react-router'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import * as authApi from '@/features/auth/api'
import { OtpCodeStep } from '@/features/auth/OtpCodeStep'
import { OtpPurpose } from '@/features/auth/types'
import { useAuth } from '@/features/auth/useAuth'

export function LoginPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [phoneNumber, setPhoneNumber] = useState('')
  const [step, setStep] = useState<'phone' | 'code'>('phone')
  const [cooldown, setCooldown] = useState(60)
  const [codeLength, setCodeLength] = useState(6)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/kabinet'

  async function handlePhoneSubmit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      const response = await authApi.requestLoginCode(phoneNumber)
      setCooldown(response.resendAfterSeconds)
      setCodeLength(response.codeLength)
      setStep('code')
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (caught.fieldError('phoneNumber') ?? caught.message)
          : 'Şəbəkə xətası. Yenidən cəhd edin.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="mx-auto w-full max-w-md py-12">
      <div className="rounded-(--radius-card) border border-line bg-surface p-6 sm:p-8">
        <h1 className="text-2xl">Giriş</h1>
        <p className="mt-2 text-sm text-muted">
          Mobil nömrənizi daxil edin — SMS ilə təsdiq kodu göndərəcəyik.
        </p>

        <div className="mt-6">
          {step === 'phone' ? (
            <form onSubmit={handlePhoneSubmit} className="flex flex-col gap-4" noValidate>
              <Input
                label="Mobil nömrə"
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                placeholder="+994 50 123 45 67"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                error={error ?? undefined}
                autoFocus
              />

              <Button type="submit" disabled={isSubmitting || phoneNumber.trim().length === 0}>
                {isSubmitting ? 'Göndərilir…' : 'Kodu göndər'}
              </Button>
            </form>
          ) : (
            <OtpCodeStep
              phoneNumber={phoneNumber}
              purpose={OtpPurpose.Login}
              codeLength={codeLength}
              initialCooldown={cooldown}
              onChangePhone={() => setStep('phone')}
              onVerified={(auth) => {
                signIn(auth)
                void navigate(redirectTo, { replace: true })
              }}
            />
          )}
        </div>

        <p className="mt-6 border-t border-line pt-4 text-sm text-muted">
          Hesabınız yoxdur?{' '}
          <NavLink to="/qeydiyyat" className="font-semibold text-interactive hover:text-accent">
            Qeydiyyatdan keçin
          </NavLink>
        </p>
      </div>
    </div>
  )
}
