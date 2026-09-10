import type { ReactNode } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { formatNumber } from '@/features/listings/format'

interface QueuePaginationProps {
  page: number
  totalPages: number
  total: number
  onChange: (page: number) => void
}

/** The one pagination control. Every queue shows its position the same way. */
export function QueuePagination({ page, totalPages, total, onChange }: QueuePaginationProps) {
  if (totalPages <= 1) {
    return <p className="text-sm text-muted">{formatNumber(total)} nəticə</p>
  }

  return (
    <nav aria-label="Səhifələr" className="flex flex-wrap items-center gap-3">
      <Button type="button" variant="secondary" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Əvvəlki
      </Button>

      <span className="text-sm text-muted">
        {page} / {totalPages} · {formatNumber(total)} nəticə
      </span>

      <Button
        type="button"
        variant="secondary"
        size="sm"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
      >
        Növbəti
      </Button>
    </nav>
  )
}

/** A filter bar. Composed from the existing form primitives; no queue rolls its own. */
export function QueueFilters({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap items-end gap-3">{children}</div>
}

type StatusTone = 'store' | 'top' | 'new' | 'neutral'

/**
 * One mapping from a domain status to its label and tone. Adding a status in one screen and
 * forgetting it in another is exactly the drift this prevents.
 */
const statusLabels: Record<string, { label: string; tone: StatusTone }> = {
  // listings
  Draft: { label: 'Qaralama', tone: 'neutral' },
  PendingModeration: { label: 'Gözləmədə', tone: 'top' },
  Active: { label: 'Aktiv', tone: 'new' },
  Rejected: { label: 'Rədd edilib', tone: 'top' },
  Expired: { label: 'Müddəti bitib', tone: 'neutral' },
  Archived: { label: 'Arxivdə', tone: 'neutral' },
  Sold: { label: 'Satılıb', tone: 'neutral' },
  Blocked: { label: 'Bloklanıb', tone: 'top' },
  // reports
  Open: { label: 'Açıq', tone: 'top' },
  Reviewing: { label: 'Baxılır', tone: 'top' },
  Resolved: { label: 'Həll edilib', tone: 'new' },
  Dismissed: { label: 'Rədd edilib', tone: 'neutral' },
  // stores
  PendingVerification: { label: 'Yoxlanılır', tone: 'top' },
  Suspended: { label: 'Dayandırılıb', tone: 'neutral' },
  // categories
  Unrestricted: { label: 'Sərbəst', tone: 'new' },
  Restricted: { label: 'Məhdud', tone: 'top' },
  Unclassified: { label: 'Təsnif edilməyib', tone: 'neutral' },
  // payment orders
  Created: { label: 'Yaradılıb', tone: 'neutral' },
  AwaitingPayment: { label: 'Ödəniş gözlənilir', tone: 'top' },
  Paid: { label: 'Ödənilib', tone: 'new' },
  Failed: { label: 'Uğursuz', tone: 'neutral' },
  Canceled: { label: 'Ləğv edilib', tone: 'neutral' },
  Refunded: { label: 'Geri qaytarılıb', tone: 'neutral' },
  PartiallyRefunded: { label: 'Qismən geri qaytarılıb', tone: 'neutral' },
  PaidAfterExpiry: { label: 'Gec ödəniş — refund gözləyir', tone: 'top' },
  // promotions
  Pending: { label: 'Gözləyir', tone: 'neutral' },
  Reversed: { label: 'Ləğv edilib', tone: 'neutral' },
}

export function StatusBadge({ status }: { status: string }) {
  const known = statusLabels[status]

  return <Badge tone={known?.tone ?? 'neutral'}>{known?.label ?? status}</Badge>
}
