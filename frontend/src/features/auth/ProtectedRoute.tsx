import { Navigate, Outlet, useLocation } from 'react-router'

import { Skeleton } from '@/components/ui/Skeleton'

import { useAuth } from './useAuth'
import type { UserRole } from './types'

interface ProtectedRouteProps {
  /** When given, the signed-in user must hold one of these roles. */
  roles?: UserRole[]

  /**
   * Where to send someone who is not signed in. The admin area points at its own screen, because
   * an administrator cannot sign in on the public one — that account is outside the SMS flow.
   */
  signInPath?: string
}

/**
 * Client-side guard. It keeps unauthenticated users out of account screens; the API enforces
 * the same rules independently, so a bypass here grants nothing.
 */
export function ProtectedRoute({ roles, signInPath = '/giris' }: ProtectedRouteProps) {
  const { isAuthenticated, isLoading, user } = useAuth()
  const location = useLocation()

  // Never redirect while the boot-time refresh is still deciding.
  if (isLoading) {
    return (
      <div className="py-10" aria-busy="true">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="mt-4 h-40 w-full" />
      </div>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to={signInPath} replace state={{ from: location.pathname }} />
  }

  if (roles && user && !roles.includes(user.role)) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
