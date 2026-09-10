import { useEffect, useState } from 'react'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

import * as authApi from './api'
import type { AuthResponse, OtpPurposeValue } from './types'
import { useResendCountdown } from './useResendCountdown'

interface OtpCodeStepProps {
  phoneNumber: string
  purpose: OtpPurposeValue
  codeLength: number
  initialCooldown: number
  onVerified: (auth: AuthResponse) => void
  onChangePhone: () => void
}

/** Second step of both sign-in and sign-up: enter the SMS code. */
export function OtpCodeStep({
  phoneNumber,
  purpose,
  codeLength,
  initialCooldown,
  onVerified,
  onChangePhone,
}: OtpCodeStepProps) {
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const { secondsLeft, canResend, start } = useResendCountdown()

  // Begin the cooldown for the code that was just sent.
  useEffect(() => {
    start(initialCooldown)
  }, [initialCooldown, start])

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setNotice(null)
    setIsSubmitting(true)

    try {
      const auth = await authApi.verifyCode(phoneNumber, code, purpose)
      onVerified(auth)
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (caught.fieldError('code') ?? caught.message)
          : 'Şəbəkə xətası. Yenidən cəhd edin.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  async function handleResend() {
    setError(null)
    setNotice(null)

    try {
      const response = await authApi.resendCode(phoneNumber, purpose)
      start(response.resendAfterSeconds)
      setNotice('Yeni kod göndərildi.')
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kod göndərilmədi.')
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <p className="text-sm text-muted">
        <span className="font-semibold text-ink">{phoneNumber}</span> nömrəsinə {codeLength} rəqəmli kod
        göndərildi.
      </p>

      <Input
        label="Təsdiq kodu"
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={codeLength}
        value={code}
        onChange={(event) => setCode(event.target.value.replace(/\D/g, ''))}
        error={error ?? undefined}
        hint={notice ?? undefined}
        autoFocus
      />

      <Button type="submit" disabled={isSubmitting || code.length < 4}>
        {isSubmitting ? 'Yoxlanılır…' : 'Təsdiqlə'}
      </Button>

      <div className="flex items-center justify-between text-sm">
        <button
          type="button"
          onClick={onChangePhone}
          className="font-medium text-interactive hover:text-accent"
        >
          Nömrəni dəyiş
        </button>

        <button
          type="button"
          onClick={() => void handleResend()}
          disabled={!canResend}
          className="font-medium text-interactive hover:text-accent disabled:text-faint"
        >
          {canResend ? 'Kodu yenidən göndər' : `Yenidən göndər (${secondsLeft}s)`}
        </button>
      </div>
    </form>
  )
}
