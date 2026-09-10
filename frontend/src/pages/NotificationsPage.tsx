import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'

import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { formatDate } from '@/features/listings/format'
import {
  getNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  notificationKeys,
} from '@/features/notifications/api'
import type { Notification } from '@/features/notifications/types'

/** "Bildirişlər" — moderation outcomes and whatever else lands in the in-app channel, newest first. */
export function NotificationsPage() {
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [markingAll, setMarkingAll] = useState(false)

  const notifications = useQuery({
    queryKey: notificationKeys.mine(page),
    queryFn: () => getNotifications(page),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
  }

  async function handleRead(notification: Notification) {
    if (notification.isRead) {
      return
    }

    await markNotificationRead(notification.id)
    invalidate()
  }

  async function handleReadAll() {
    setMarkingAll(true)

    try {
      await markAllNotificationsRead()
      invalidate()
    } finally {
      setMarkingAll(false)
    }
  }

  const hasUnread = notifications.data?.items.some((n) => !n.isRead) ?? false

  return (
    <div className="flex flex-col gap-5 py-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-ink">Bildirişlər</h1>

        {hasUnread ? (
          <Button variant="secondary" size="sm" disabled={markingAll} onClick={() => void handleReadAll()}>
            {markingAll ? 'İşarələnir…' : 'Hamısını oxunmuş et'}
          </Button>
        ) : null}
      </header>

      {notifications.isPending ? (
        <div className="flex flex-col gap-3" aria-busy="true">
          <Skeleton className="h-20" />
          <Skeleton className="h-20" />
          <Skeleton className="h-20" />
        </div>
      ) : notifications.isError ? (
        <ErrorState
          description="Bildirişləri yükləmək mümkün olmadı."
          onRetry={() => void notifications.refetch()}
        />
      ) : notifications.data.items.length === 0 ? (
        <EmptyState title="Hələ bildiriş yoxdur." description="Elanlarınızla bağlı yeniliklər burada görünəcək." />
      ) : (
        <ul className="flex flex-col gap-2">
          {notifications.data.items.map((notification) => (
            <li key={notification.id}>
              <button
                type="button"
                onClick={() => void handleRead(notification)}
                className={`flex w-full flex-col gap-1 rounded-(--radius-card) border p-4 text-left transition-colors ${
                  notification.isRead
                    ? 'border-line bg-surface'
                    : 'border-interactive/40 bg-interactive-soft'
                }`}
              >
                <div className="flex items-start justify-between gap-3">
                  <span className="font-semibold text-ink">{notification.title}</span>
                  {notification.isRead ? null : (
                    <span className="mt-1.5 size-2 shrink-0 rounded-full bg-interactive" aria-hidden="true" />
                  )}
                </div>

                <p className="text-sm text-muted">{notification.body}</p>
                <p className="text-xs text-faint">{formatDate(notification.createdAt)}</p>
              </button>
            </li>
          ))}
        </ul>
      )}

      {notifications.data && notifications.data.totalPages > 1 ? (
        <nav aria-label="Səhifələr" className="flex items-center justify-center gap-3 pt-2">
          <Button
            variant="secondary"
            size="sm"
            disabled={page <= 1}
            onClick={() => setPage((current) => current - 1)}
          >
            Əvvəlki
          </Button>
          <span className="text-sm text-muted">
            {page} / {notifications.data.totalPages}
          </span>
          <Button
            variant="secondary"
            size="sm"
            disabled={page >= notifications.data.totalPages}
            onClick={() => setPage((current) => current + 1)}
          >
            Növbəti
          </Button>
        </nav>
      ) : null}
    </div>
  )
}
