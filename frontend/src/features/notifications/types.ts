/** Mirrors NotificationDto on the API. */
export interface Notification {
  id: string
  type: string
  title: string
  body: string
  entityType: string | null
  entityId: string | null
  isRead: boolean
  createdAt: string
}

export interface UnreadNotificationCount {
  count: number
}
