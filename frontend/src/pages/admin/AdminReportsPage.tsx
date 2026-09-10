import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Select'
import { adminKeys, dismissReport, getReports, resolveReport } from '@/features/admin/api'
import { ConfirmDialog } from '@/features/admin/components/ActionDialog'
import { QueueFilters, QueuePagination, StatusBadge } from '@/features/admin/components/QueueChrome'
import { waitingFor } from '@/features/admin/format'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminReport } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { listingPath } from '@/features/listings/format'

const reasonLabels: Record<string, string> = {
  Prohibited: 'Qadağan olunmuş məhsul',
  Fraud: 'Fırıldaqçılıq',
  WrongCategory: 'Yanlış kateqoriya',
  Duplicate: 'Təkrar elan',
  MisleadingPrice: 'Yanlış qiymət',
  ForeignPhotos: 'Özgə şəkilləri',
  Other: 'Digər',
}

/**
 * What visitors have flagged.
 *
 * Closing a report is not the same as acting on the listing: the listing's status only ever changes
 * through the moderation endpoints, so this screen closes the report and offers a way through to
 * moderation rather than quietly doing both.
 */
export function AdminReportsPage() {
  const { values, update, page, goToPage } = useAdminQueue({ status: 'Open' })
  const [pending, setPending] = useState<{ report: AdminReport; action: 'resolve' | 'dismiss' } | null>(null)

  const queue = useQuery({
    queryKey: adminKeys.reports(values),
    queryFn: () => getReports(values),
  })

  const close = useAdminAction<{ id: string; action: 'resolve' | 'dismiss' }>({
    action: ({ id, action }) => (action === 'resolve' ? resolveReport(id) : dismissReport(id)),
    invalidate: [adminKeys.reports(values), adminKeys.overview],
    onSuccess: () => setPending(null),
  })

  const columns: QueueColumn<AdminReport>[] = [
    {
      key: 'listing',
      header: 'Elan',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-ink">{row.listingTitle}</span>
          <span className="text-xs text-muted">№ {row.listingShortId}</span>
        </div>
      ),
    },
    {
      key: 'reason',
      header: 'Səbəb',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-ink">{reasonLabels[row.reason] ?? row.reason}</span>
          {row.comment ? <span className="text-xs text-muted">{row.comment}</span> : null}
        </div>
      ),
    },
    {
      key: 'source',
      header: 'Mənbə',
      secondary: true,
      render: (row) => (row.isAnonymous ? <Badge>Anonim</Badge> : <Badge tone="neutral">Qeydiyyatlı</Badge>),
    },
    { key: 'status', header: 'Status', secondary: true, render: (row) => <StatusBadge status={row.status} /> },
    {
      key: 'waiting',
      header: 'Gözləyir',
      align: 'right',
      render: (row) => <span className="text-muted">{waitingFor(row.createdAt)}</span>,
    },
    {
      key: 'actions',
      header: 'Əməliyyat',
      align: 'right',
      render: (row) =>
        row.status === 'Open' || row.status === 'Reviewing' ? (
          <div className="flex flex-wrap justify-end gap-1.5">
            <Button
              type="button"
              size="sm"
              variant="secondary"
              onClick={() => setPending({ report: row, action: 'resolve' })}
            >
              Həll et
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={() => setPending({ report: row, action: 'dismiss' })}
            >
              Rədd et
            </Button>
          </div>
        ) : (
          <Link
            to={listingPath('elan', row.listingShortId)}
            className="text-sm text-interactive hover:underline"
          >
            Elana bax
          </Link>
        ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">Şikayətlər</h1>
        <p className="text-sm text-muted">
          Şikayəti bağlamaq elanın statusunu dəyişmir — elana qərar moderasiya bölməsində verilir.
        </p>
      </header>

      <QueueFilters>
        <Select
          label="Status"
          value={values['status'] ?? 'Open'}
          onChange={(event) => update({ status: event.target.value })}
        >
          <option value="Open">Açıq</option>
          <option value="Resolved">Həll edilib</option>
          <option value="Dismissed">Rədd edilib</option>
        </Select>
      </QueueFilters>

      {close.state.kind === 'conflict' ? (
        <p role="status" className="rounded-(--radius-input) border border-line bg-surface px-3 py-2 text-sm text-ink">
          {close.state.message} Siyahı yeniləndi.
        </p>
      ) : null}

      <QueueTable
        columns={columns}
        rows={queue.data?.items}
        rowKey={(row) => row.id}
        isPending={queue.isPending}
        isError={queue.isError}
        onRetry={() => void queue.refetch()}
        emptyTitle="Şikayət yoxdur."
        errorDescription="Şikayətləri yükləmək mümkün olmadı."
      />

      {queue.data ? (
        <QueuePagination
          page={page}
          totalPages={queue.data.totalPages}
          total={queue.data.total}
          onChange={goToPage}
        />
      ) : null}

      {pending ? (
        <ConfirmDialog
          title={pending.action === 'resolve' ? 'Şikayəti həll edilmiş kimi bağla' : 'Şikayəti rədd et'}
          description={
            pending.action === 'resolve'
              ? 'Şikayət əsaslı sayılır və bağlanır. Elanın statusu dəyişmir — bunun üçün moderasiya bölməsindən istifadə edin.'
              : 'Şikayət əsassız sayılır və bağlanır. Elan olduğu kimi qalır.'
          }
          confirmLabel={pending.action === 'resolve' ? 'Həll edildi' : 'Rədd et'}
          busy={close.isPending}
          onConfirm={() => close.run({ id: pending.report.id, action: pending.action })}
          onCancel={() => setPending(null)}
        />
      ) : null}
    </div>
  )
}
