import { createContext } from 'react'

import type { AuthResponse, User } from './types'

export interface AuthContextValue {
  user: User | null
  /** True until the boot-time session restore finishes, so guards do not redirect too early. */
  isLoading: boolean
  isAuthenticated: boolean
  /** Stores the token and user returned by a successful verification. */
  signIn: (auth: AuthResponse) => void
  signOut: () => Promise<void>
  setUser: (user: User) => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)
