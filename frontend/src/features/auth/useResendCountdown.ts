import { useCallback, useEffect, useState } from 'react'

/** Counts down the server-declared resend cooldown so the button cannot be hammered. */
export function useResendCountdown() {
  const [secondsLeft, setSecondsLeft] = useState(0)

  useEffect(() => {
    if (secondsLeft <= 0) {
      return
    }

    const timer = setTimeout(() => setSecondsLeft((value) => value - 1), 1000)
    return () => clearTimeout(timer)
  }, [secondsLeft])

  const start = useCallback((seconds: number) => setSecondsLeft(Math.max(0, seconds)), [])

  return { secondsLeft, canResend: secondsLeft <= 0, start }
}
