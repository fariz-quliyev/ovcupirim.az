import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'

import { setAccessToken } from '@/api/authToken'
import { refreshSession } from '@/api/client'

import { AuthContext, type AuthContextValue } from './AuthContext'
import * as authApi from './api'
import type { AuthResponse, User } from './types'

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUserState] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const queryClient = useQueryClient()

  // On boot the access token is gone (it only ever lived in memory), but the HttpOnly refresh
  // cookie may still be valid — so try once to restore the session. This goes through the client's
  // shared refresh rather than its own fetch: refresh tokens rotate, and a second overlapping call
  // presents a spent token, which the API correctly reads as reuse and answers by revoking every
  // session. Development's double-invoked effects make that overlap happen on every single boot.
  useEffect(() => {
    let cancelled = false

    void (async () => {
      const auth = await refreshSession<AuthResponse>()

      if (cancelled) {
        return
      }

      setUserState(auth?.user ?? null)
      setIsLoading(false)
    })()

    return () => {
      cancelled = true
    }
  }, [])

  const signIn = useCallback((auth: AuthResponse) => {
    setAccessToken(auth.accessToken)
    setUserState(auth.user)
  }, [])

  const signOut = useCallback(async () => {
    try {
      await authApi.logout()
    } catch {
      // Revoking server-side is best effort: if the network is down the local session still ends,
      // and the refresh token expires on its own. Signing out must never fail for the user.
    } finally {
      setAccessToken(null)
      setUserState(null)
      queryClient.clear()
    }
  }, [queryClient])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isLoading,
      isAuthenticated: user !== null,
      signIn,
      signOut,
      setUser: setUserState,
    }),
    [user, isLoading, signIn, signOut],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
