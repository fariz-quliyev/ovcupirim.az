import { useState } from 'react'
import { NavLink, useNavigate } from 'react-router'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import * as authApi from '@/features/auth/api'
import { OtpCodeStep } from '@/features/auth/OtpCodeStep'
import { OtpPurpose } from '@/features/auth/types'
import { useAuth } from '@/features/auth/useAuth'

export function RegisterPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()

  const [phoneNumber, setPhoneNumber] = useState('')
  const [fullName, setFullName] = useState('')
  const [step, setStep] = useState<'form' | 'code'>('form')
  const [cooldown, setCooldown] = useState(60)
  const [codeLength, setCodeLength] = useState(6)
  const [errors, setErrors] = useState<{ phoneNumber?: string; fullName?: string; general?: string }>({})
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setErrors({})
    setIsSubmitting(true)

    try {
      const response = await authApi.register(phoneNumber, fullName)
      setCooldown(response.resendAfterSeconds)
      setCodeLength(response.codeLength)
      setStep('code')
    } catch (caught) {
      if (caught instanceof ApiError) {
        const phoneError = caught.fieldError('phoneNumber')
        const nameError = caught.fieldError('fullName')

        setErrors({
          ...(phoneError ? { phoneNumber: phoneError } : {}),
          ...(nameError ? { fullName: nameError } : {}),
          ...(phoneError || nameError ? {} : { general: caught.message }),
        })
      } else {
        setErrors({ general: 'Şəbəkə xətası. Yenidən cəhd edin.' })
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="mx-auto w-full max-w-md py-12">
      <div className="rounded-(--radius-card) border border-line bg-surface p-6 sm:p-8">
        <h1 className="text-2xl">Qeydiyyat</h1>
        <p className="mt-2 text-sm text-muted">
          Elan yerləşdirmək üçün mobil nömrənizi təsdiqləyin.
        </p>

        <div className="mt-6">
          {step === 'form' ? (
            <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
              <Input
                label="Ad və soyad"
                autoComplete="name"
                placeholder="Aydın Məmmədov"
                value={fullName}
                onChange={(event) => setFullName(event.target.value)}
                error={errors.fullName}
                autoFocus
              />

              <Input
                label="Mobil nömrə"
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                placeholder="+994 50 123 45 67"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                error={errors.phoneNumber}
              />

              {errors.general ? (
                <p role="alert" className="text-sm text-accent">
                  {errors.general}
                </p>
              ) : null}

              <Button
                type="submit"
                disabled={isSubmitting || phoneNumber.trim().length === 0 || fullName.trim().length === 0}
              >
                {isSubmitting ? 'Göndərilir…' : 'Davam et'}
              </Button>

              <p className="text-xs text-muted">
                Davam etməklə{' '}
                <NavLink to="/melumat/istifadeci-razilasmasi" className="underline">
                  İstifadəçi razılaşması
                </NavLink>{' '}
                ilə razılaşırsınız.
              </p>
            </form>
          ) : (
            <OtpCodeStep
              phoneNumber={phoneNumber}
              purpose={OtpPurpose.Registration}
              codeLength={codeLength}
              initialCooldown={cooldown}
              onChangePhone={() => setStep('form')}
              onVerified={(auth) => {
                signIn(auth)
                void navigate('/kabinet', { replace: true })
              }}
            />
          )}
        </div>

        <p className="mt-6 border-t border-line pt-4 text-sm text-muted">
          Artıq hesabınız var?{' '}
          <NavLink to="/giris" className="font-semibold text-interactive hover:text-accent">
            Giriş edin
          </NavLink>
        </p>
      </div>
    </div>
  )
}
