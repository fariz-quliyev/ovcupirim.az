import { useQuery } from '@tanstack/react-query'
import { NavLink } from 'react-router'

import { useAuth } from '@/features/auth/useAuth'

import { getUnreadNotificationCount, notificationKeys } from './api'

/**
 * The header's unread badge. Polling rather than a live connection: there is no push channel yet
 * (only the in-app channel exists), and a moderation decision is not so time-sensitive that a
 * seller needs to learn about it within seconds rather than within a minute.
 */
export function NotificationBell() {
  const { isAuthenticated } = useAuth()

  const unread = useQuery({
    queryKey: notificationKeys.unreadCount,
    queryFn: getUnreadNotificationCount,
    enabled: isAuthenticated,
    refetchInterval: 60_000,
    staleTime: 30_000,
  })

  if (!isAuthenticated) {
    return null
  }

  const count = unread.data?.count ?? 0

  return (
    <NavLink
      to="/kabinet/bildirisler"
      aria-label={count > 0 ? `Bildirişlər, ${count} oxunmamış` : 'Bildirişlər'}
      className="relative flex size-9 items-center justify-center rounded-full text-white/90 transition-colors hover:bg-white/10 hover:text-white"
    >
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" className="size-5" aria-hidden="true">
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          d="M18 8a6 6 0 1 0-12 0c0 5.5-2 7-2 7h16s-2-1.5-2-7"
        />
        <path strokeLinecap="round" strokeLinejoin="round" d="M13.73 21a2 2 0 0 1-3.46 0" />
      </svg>

      {count > 0 ? (
        <span
          className="absolute top-1 right-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-accent px-1 text-[10px] font-semibold leading-none text-white"
          aria-hidden="true"
        >
          {count > 9 ? '9+' : count}
        </span>
      ) : null}
    </NavLink>
  )
}
