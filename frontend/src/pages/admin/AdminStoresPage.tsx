import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Select'
import {
  adminKeys,
  approveStore,
  getAdminStores,
  reinstateStore,
  rejectStore,
  setStoreVerified,
  suspendStore,
} from '@/features/admin/api'
import { ConfirmDialog, ReasonDialog } from '@/features/admin/components/ActionDialog'
import { QueueFilters, QueuePagination, StatusBadge } from '@/features/admin/components/QueueChrome'
import { waitingFor } from '@/features/admin/format'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminStore } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { storePath } from '@/features/stores/types'

type StoreAction = 'approve' | 'reject' | 'suspend' | 'reinstate' | 'verify' | 'unverify'

/** Suspension is the one that surprises people, so the dialog says what it actually does. */
const consequences: Record<Exclude<StoreAction, 'reject' | 'suspend'>, string> = {
  approve: 'Mağaza saytda görünəcək və satıcı elanlarını ona bağlaya biləcək.',
  reinstate: 'Mağaza yenidən saytda görünəcək.',
  verify: 'Mağazaya "Təsdiqlənmiş" nişanı veriləcək.',
  unverify: '"Təsdiqlənmiş" nişanı götürüləcək.',
}

export function AdminStoresPage() {
  const { values, update, page, goToPage } = useAdminQueue({ status: 'PendingVerification' })
  const [pending, setPending] = useState<{ store: AdminStore; action: StoreAction } | null>(null)

  const queue = useQuery({
    queryKey: adminKeys.stores(values),
    queryFn: () => getAdminStores(values),
  })

  const act = useAdminAction<{ store: AdminStore; action: StoreAction; reason: string }>({
    action: ({ store, action, reason }) => {
      switch (action) {
        case 'approve':
          return approveStore(store.id)
        case 'reject':
          return rejectStore(store.id, reason)
        case 'suspend':
          return suspendStore(store.id, reason)
        case 'reinstate':
          return reinstateStore(store.id)
        case 'verify':
          return setStoreVerified(store.id, true)
        case 'unverify':
          return setStoreVerified(store.id, false)
      }
    },
    invalidate: [adminKeys.stores(values), adminKeys.overview],
    onSuccess: () => setPending(null),
  })

  const columns: QueueColumn<AdminStore>[] = [
    {
      key: 'store',
      header: 'Mağaza',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 font-medium text-ink">
            {row.name}
            {row.isVerified ? <Badge tone="store">Təsdiqlənmiş</Badge> : null}
          </span>
          <span className="text-xs text-muted">/{row.slug}</span>
        </div>
      ),
    },
    {
      key: 'owner',
      header: 'Sahib',
      secondary: true,
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-ink">{row.ownerName}</span>
          {row.phone ? <span className="text-xs text-muted">{row.phone}</span> : null}
        </div>
      ),
    },
    { key: 'listings', header: 'Elan', secondary: true, align: 'right', render: (row) => row.listingCount },
    { key: 'status', header: 'Status', render: (row) => <StatusBadge status={row.status} /> },
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
      render: (row) => (
        <div className="flex flex-wrap justify-end gap-1.5">
          {row.status === 'PendingVerification' ? (
            <>
              <Button type="button" size="sm" onClick={() => setPending({ store: row, action: 'approve' })}>
                Təsdiqlə
              </Button>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                onClick={() => setPending({ store: row, action: 'reject' })}
              >
                Rədd et
              </Button>
            </>
          ) : null}

          {row.status === 'Active' ? (
            <>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                onClick={() => setPending({ store: row, action: row.isVerified ? 'unverify' : 'verify' })}
              >
                {row.isVerified ? 'Nişanı götür' : 'Nişan ver'}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => setPending({ store: row, action: 'suspend' })}
              >
                Dayandır
              </Button>
              <Link to={storePath(row.slug)} className="self-center text-sm text-interactive hover:underline">
                Bax
              </Link>
            </>
          ) : null}

          {row.status === 'Suspended' ? (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              onClick={() => setPending({ store: row, action: 'reinstate' })}
            >
              Bərpa et
            </Button>
          ) : null}
        </div>
      ),
    },
  ]

  const needsReason = pending?.action === 'reject' || pending?.action === 'suspend'

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">Mağaza müraciətləri</h1>
        <p className="text-sm text-muted">Müraciətlər, aktiv mağazalar və dayandırılmışlar.</p>
      </header>

      <QueueFilters>
        <Select
          label="Status"
          value={values['status'] ?? 'PendingVerification'}
          onChange={(event) => update({ status: event.target.value })}
        >
          <option value="PendingVerification">Yoxlanılır</option>
          <option value="Active">Aktiv</option>
          <option value="Suspended">Dayandırılıb</option>
        </Select>
      </QueueFilters>

      {act.state.kind === 'conflict' ? (
        <p role="status" className="rounded-(--radius-input) border border-line bg-surface px-3 py-2 text-sm text-ink">
          {act.state.message} Siyahı yeniləndi.
        </p>
      ) : null}

      {act.state.kind === 'error' ? (
        <p role="alert" className="text-sm text-accent">
          {act.state.message}
        </p>
      ) : null}

      <QueueTable
        columns={columns}
        rows={queue.data?.items}
        rowKey={(row) => row.id}
        isPending={queue.isPending}
        isError={queue.isError}
        onRetry={() => void queue.refetch()}
        emptyTitle="Bu statusda mağaza yoxdur."
        errorDescription="Mağazaları yükləmək mümkün olmadı."
      />

      {queue.data ? (
        <QueuePagination
          page={page}
          totalPages={queue.data.totalPages}
          total={queue.data.total}
          onChange={goToPage}
        />
      ) : null}

      {pending && needsReason ? (
        <ReasonDialog
          title={pending.action === 'reject' ? 'Müraciəti rədd et' : 'Mağazanı dayandır'}
          description={
            pending.action === 'reject'
              ? 'Müraciət geri götürülür və satıcı yenidən müraciət edə bilər. Səbəb audit jurnalında saxlanılır.'
              : 'Mağaza saytda görünməyi dayandırır. Satıcının elanları isə saytda qalır və satışda olur.'
          }
          confirmLabel={pending.action === 'reject' ? 'Rədd et' : 'Dayandır'}
          error={act.fieldError}
          busy={act.isPending}
          onConfirm={(reason) => act.run({ store: pending.store, action: pending.action, reason })}
          onCancel={() => {
            act.reset()
            setPending(null)
          }}
        />
      ) : null}

      {pending && !needsReason ? (
        <ConfirmDialog
          title={pending.store.name}
          description={consequences[pending.action as Exclude<StoreAction, 'reject' | 'suspend'>]}
          confirmLabel="Təsdiq et"
          busy={act.isPending}
          onConfirm={() => act.run({ store: pending.store, action: pending.action, reason: '' })}
          onCancel={() => setPending(null)}
        />
      ) : null}
    </div>
  )
}
