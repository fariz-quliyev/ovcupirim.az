import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { ErrorState } from '@/components/ui/ErrorState'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import {
  adminKeys,
  approveListing,
  blockListing,
  getModerationDetail,
  getModerationQueue,
  rejectListing,
  unblockListing,
} from '@/features/admin/api'
import { ConfirmDialog, ReasonDialog } from '@/features/admin/components/ActionDialog'
import { ModerationHistory } from '@/features/admin/components/AuditTrail'
import { QueueFilters, QueuePagination, StatusBadge } from '@/features/admin/components/QueueChrome'
import { waitingFor } from '@/features/admin/format'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { ModerationQueueItem } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { formatDate, formatPrice } from '@/features/listings/format'

type PendingAction = 'reject' | 'block' | 'unblock' | null

/**
 * The listing moderation queue and its decision pane.
 *
 * The pane is the point of the screen: a queue row cannot tell a moderator whether to approve
 * something, so opening one shows the images, the description, the attributes and the decisions
 * already taken. Every decision goes to the server as-is — nothing about the listing state machine
 * is reimplemented here.
 */
export function AdminModerationPage() {
  const { values, update, page, goToPage } = useAdminQueue({ status: 'pending' })
  const [openId, setOpenId] = useState<string | null>(null)

  const status = values['status'] === 'active' ? 'active' : 'pending'

  const queue = useQuery({
    queryKey: adminKeys.moderationQueue(values),
    queryFn: () => getModerationQueue(values),
  })

  const columns: QueueColumn<ModerationQueueItem>[] = [
    {
      key: 'title',
      header: 'Elan',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-ink">{row.title}</span>
          <span className="text-xs text-muted">
            № {row.shortId} · {row.categoryNameAz}
          </span>
        </div>
      ),
    },
    {
      key: 'restriction',
      header: 'Təsnifat',
      secondary: true,
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          <StatusBadge status={row.restrictionStatus} />
          {row.screeningFlags.length > 0 ? <Badge tone="top">{row.screeningFlags.length} işarə</Badge> : null}
        </div>
      ),
    },
    { key: 'seller', header: 'Satıcı', secondary: true, render: (row) => row.sellerName },
    {
      key: 'waiting',
      // Same column, same underlying timestamp — only the label changes: an active listing was not
      // "waiting" for anything, it was last touched.
      header: status === 'active' ? 'Yenilənib' : 'Gözləyir',
      align: 'right',
      render: (row) => <span className="text-muted">{waitingFor(row.submittedAt)}</span>,
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">Elan moderasiyası</h1>
        <p className="text-sm text-muted">
          {status === 'active'
            ? 'Saytda olan elanlar. Qaydaları pozan bir elanı bura buraxmaq üçün bloklaya bilərsiniz.'
            : 'Dərc üçün gözləyən elanlar.'}
        </p>
      </header>

      <QueueFilters>
        <Select
          label="Növbə"
          value={status}
          onChange={(event) => update({ status: event.target.value })}
        >
          <option value="pending">Gözləyən</option>
          <option value="active">Aktiv</option>
        </Select>

        <Checkbox
          label="Yalnız diqqət tələb edənlər"
          checked={values['strict'] === 'true'}
          onChange={(event) => update({ strict: event.target.checked ? 'true' : null })}
          hint="Məhdud və təsnif edilməmiş kateqoriyalar."
        />
      </QueueFilters>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div className="flex flex-col gap-3">
          <QueueTable
            columns={columns}
            rows={queue.data?.items}
            rowKey={(row) => row.id}
            isPending={queue.isPending}
            isError={queue.isError}
            onRetry={() => void queue.refetch()}
            emptyTitle="Növbə boşdur."
            emptyDescription={status === 'active' ? 'Aktiv elan yoxdur.' : 'Gözləyən elan yoxdur.'}
            errorDescription="Növbəni yükləmək mümkün olmadı."
            selectedKey={openId ?? undefined}
            onSelect={(row) => setOpenId(row.id)}
          />

          {queue.data ? (
            <QueuePagination
              page={page}
              totalPages={queue.data.totalPages}
              total={queue.data.total}
              onChange={goToPage}
            />
          ) : null}
        </div>

        {openId ? (
          <ModerationDetailPane
            id={openId}
            queryKey={adminKeys.moderationQueue(values)}
            onClose={() => setOpenId(null)}
          />
        ) : (
          <aside className="hidden rounded-(--radius-card) border border-dashed border-line px-4 py-10 text-center text-sm text-muted xl:block">
            Qərar vermək üçün siyahıdan bir elan seçin.
          </aside>
        )}
      </div>
    </div>
  )
}

interface DetailPaneProps {
  id: string
  queryKey: readonly unknown[]
  onClose: () => void
}

function ModerationDetailPane({ id, queryKey, onClose }: DetailPaneProps) {
  const [pending, setPending] = useState<PendingAction>(null)

  const detail = useQuery({
    queryKey: adminKeys.moderationDetail(id),
    queryFn: () => getModerationDetail(id),
    retry: false,
  })

  const invalidate = [queryKey, adminKeys.moderationDetail(id), adminKeys.overview]

  const approve = useAdminAction({
    action: () => approveListing(id),
    invalidate,
    onSuccess: onClose,
  })

  const decide = useAdminAction<{ action: Exclude<PendingAction, null>; reason: string }>({
    action: ({ action, reason }) =>
      action === 'reject'
        ? rejectListing(id, reason)
        : action === 'block'
          ? blockListing(id, reason)
          : unblockListing(id),
    invalidate,
    onSuccess: () => {
      setPending(null)
      onClose()
    },
  })

  if (detail.isPending) {
    return <Skeleton className="h-96" />
  }

  if (detail.isError || !detail.data) {
    return <ErrorState description="Elanı yükləmək mümkün olmadı." onRetry={() => void detail.refetch()} />
  }

  const listing = detail.data.listing
  const conflict = approve.state.kind === 'conflict' ? approve.state : decide.state.kind === 'conflict' ? decide.state : null
  const failure = approve.state.kind === 'error' ? approve.state : decide.state.kind === 'error' ? decide.state : null

  return (
    <aside className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <h2 className="text-base font-semibold text-ink">{listing.title}</h2>
          <p className="text-xs text-muted">
            № {listing.shortId} · {listing.categoryNameAz} · {formatDate(listing.createdAt)}
          </p>
        </div>

        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Bağla
        </Button>
      </div>

      {conflict ? (
        <p role="status" className="rounded-(--radius-input) border border-line bg-canvas px-3 py-2 text-sm text-ink">
          {conflict.message} Növbə yeniləndi.
        </p>
      ) : null}

      {failure ? (
        <p role="alert" className="text-sm text-accent">
          {failure.message}
        </p>
      ) : null}

      {listing.media.length > 0 ? (
        <ul className="grid grid-cols-3 gap-2">
          {listing.media.map((image) => (
            <li key={image.id}>
              <img
                src={image.variants['card'] ?? image.url}
                alt=""
                className="aspect-4/3 w-full rounded-(--radius-input) border border-line object-cover"
              />
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted">Şəkil yoxdur.</p>
      )}

      <dl className="divide-y divide-line rounded-(--radius-input) border border-line text-sm">
        <Row label="Qiymət" value={formatPrice(listing.price, listing.currency)} />
        <Row label="Satıcı" value={detail.data.sellerName} />
        {/* Unmasked here and only here (PD-7.5): the public page still hides it behind a reveal. */}
        <Row label="Əlaqə nömrəsi" value={listing.contactPhone} />
        <Row label="Şəhər" value={listing.regionNameAz} />
        <Row label="Vəziyyət" value={listing.condition === 'New' ? 'Yeni' : 'İşlənmiş'} />
        {listing.displayAttributes.map((attribute) => (
          <Row key={attribute.key} label={attribute.labelAz} value={attribute.displayValue} />
        ))}
      </dl>

      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-ink">Təsvir</h3>
        <p className="whitespace-pre-line text-sm leading-relaxed text-ink">{listing.description}</p>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-ink">Qərar tarixçəsi</h3>
        <ModerationHistory history={detail.data.history} />
      </section>

      <div className="flex flex-wrap gap-2">
        {listing.status === 'PendingModeration' ? (
          <>
            <Button
              type="button"
              size="sm"
              disabled={approve.isPending || decide.isPending}
              onClick={() => approve.run(undefined as never)}
            >
              Təsdiqlə
            </Button>

            <Button type="button" variant="secondary" size="sm" onClick={() => setPending('reject')}>
              Rədd et
            </Button>
          </>
        ) : null}

        {listing.status === 'Blocked' ? (
          <Button type="button" variant="secondary" size="sm" onClick={() => setPending('unblock')}>
            Bloku götür
          </Button>
        ) : listing.status === 'PendingModeration' || listing.status === 'Active' ? (
          // Blocking reaches further than a rejection: it is available on a pending listing too,
          // for the rare case where a moderator does not need to see a reject reason on file — the
          // block reason already says why.
          <Button type="button" variant="secondary" size="sm" onClick={() => setPending('block')}>
            Blokla
          </Button>
        ) : null}
      </div>

      {pending === 'reject' || pending === 'block' ? (
        <ReasonDialog
          title={pending === 'reject' ? 'Elanı rədd et' : 'Elanı blokla'}
          description={
            pending === 'reject'
              ? 'Səbəb satıcıya göstərilir və audit jurnalında saxlanılır.'
              : 'Elan saytdan çıxarılır. Səbəb audit jurnalında saxlanılır.'
          }
          confirmLabel={pending === 'reject' ? 'Rədd et' : 'Blokla'}
          error={decide.fieldError}
          busy={decide.isPending}
          onConfirm={(reason) => decide.run({ action: pending, reason })}
          onCancel={() => {
            decide.reset()
            setPending(null)
          }}
        />
      ) : null}

      {pending === 'unblock' ? (
        <ConfirmDialog
          title="Bloku götür"
          description="Elan yenidən saytda görünəcək."
          confirmLabel="Bloku götür"
          busy={decide.isPending}
          onConfirm={() => decide.run({ action: 'unblock', reason: '' })}
          onCancel={() => setPending(null)}
        />
      ) : null}
    </aside>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-3 py-2">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right text-ink">{value}</dd>
    </div>
  )
}
