import { api } from '@/api/client'
import type { PagedResult } from '@/features/listings/types'

import type { Notification, UnreadNotificationCount } from './types'

export const notificationKeys = {
  mine: (page: number) => ['notifications', 'mine', page] as const,
  unreadCount: ['notifications', 'unread-count'] as const,
}

export function getNotifications(page = 1): Promise<PagedResult<Notification>> {
  return api.get<PagedResult<Notification>>('/me/notifications', { query: { page } })
}

export function getUnreadNotificationCount(): Promise<UnreadNotificationCount> {
  return api.get<UnreadNotificationCount>('/me/notifications/unread-count')
}

export function markNotificationRead(id: string): Promise<void> {
  return api.post<void>(`/me/notifications/${id}/read`)
}

export function markAllNotificationsRead(): Promise<void> {
  return api.post<void>('/me/notifications/read-all')
}
