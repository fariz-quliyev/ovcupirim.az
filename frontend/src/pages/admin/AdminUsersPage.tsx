import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Skeleton'
import { adminKeys, getUser, getUsers, setModerator } from '@/features/admin/api'
import { ConfirmDialog } from '@/features/admin/components/ActionDialog'
import { QueueFilters, QueuePagination } from '@/features/admin/components/QueueChrome'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminUser } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { useAuth } from '@/features/auth/useAuth'
import { formatDate } from '@/features/listings/format'

/**
 * User administration — inspect-only (PD-7.6).
 *
 * There is no ban, suspend, delete, force-logout or profile edit here, and no API behind any of
 * them. The only mutation is the Moderator role (PD-7.2): Admin is granted out of band, and an
 * administrator cannot change their own role.
 */
export function AdminUsersPage() {
  const { values, update, page, goToPage } = useAdminQueue()
  const { user: self } = useAuth()
  const [pending, setPending] = useState<{ target: AdminUser; grant: boolean } | null>(null)
  const [inspecting, setInspecting] = useState<string | null>(null)

  const users = useQuery({
    queryKey: adminKeys.users(values),
    queryFn: () => getUsers(values),
  })

  const role = useAdminAction<{ id: string; grant: boolean }>({
    action: ({ id, grant }) => setModerator(id, grant),
    invalidate: [adminKeys.users(values)],
    onSuccess: () => setPending(null),
  })

  const columns: QueueColumn<AdminUser>[] = [
    {
      key: 'user',
      header: 'İstifadəçi',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-ink">{row.fullName}</span>
          <span className="text-xs text-muted">{row.phoneNumber}</span>
        </div>
      ),
    },
    {
      key: 'role',
      header: 'Rol',
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          <Badge tone={row.role === 'Admin' ? 'top' : row.role === 'Moderator' ? 'store' : 'neutral'}>
            {row.role}
          </Badge>
          {row.isPhoneVerified ? <Badge tone="new">Təsdiqlənib</Badge> : null}
        </div>
      ),
    },
    { key: 'email', header: 'E-mail', secondary: true, render: (row) => row.email ?? '—' },
    {
      key: 'joined',
      header: 'Qeydiyyat',
      secondary: true,
      align: 'right',
      render: (row) => <span className="text-muted">{formatDate(row.createdAt)}</span>,
    },
    {
      key: 'actions',
      header: 'Rol idarəetməsi',
      align: 'right',
      render: (row) => {
        // Admin is untouchable here, and so is the signed-in operator's own account.
        if (row.role === 'Admin' || row.id === self?.id) {
          return <span className="text-xs text-faint">—</span>
        }

        return (
          <Button
            type="button"
            size="sm"
            variant="secondary"
            onClick={() => setPending({ target: row, grant: row.role !== 'Moderator' })}
          >
            {row.role === 'Moderator' ? 'Moderatorluğu götür' : 'Moderator et'}
          </Button>
        )
      },
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">İstifadəçilər</h1>
        <p className="text-sm text-muted">
          Yalnız baxış və moderator rolunun idarəsi. Hesab bloklama, silmə və ya profil redaktəsi yoxdur.
        </p>
      </header>

      <QueueFilters>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            const value = String(new FormData(event.currentTarget).get('q') ?? '')
            update({ q: value.trim() === '' ? null : value.trim() })
          }}
        >
          <Input label="Axtarış" name="q" defaultValue={values['q'] ?? ''} placeholder="Ad və ya nömrə" />
        </form>
      </QueueFilters>

      {role.state.kind === 'conflict' || role.state.kind === 'error' ? (
        <p
          role={role.state.kind === 'conflict' ? 'status' : 'alert'}
          className={`text-sm ${role.state.kind === 'conflict' ? 'text-ink' : 'text-accent'}`}
        >
          {role.state.message}
        </p>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,22rem)]">
        <div className="flex flex-col gap-3">
          <QueueTable
            columns={columns}
            rows={users.data?.items}
            rowKey={(row) => row.id}
            isPending={users.isPending}
            isError={users.isError}
            onRetry={() => void users.refetch()}
            emptyTitle="İstifadəçi tapılmadı."
            errorDescription="İstifadəçiləri yükləmək mümkün olmadı."
            selectedKey={inspecting ?? undefined}
            onSelect={(row) => setInspecting(row.id)}
          />

          {users.data ? (
            <QueuePagination
              page={page}
              totalPages={users.data.totalPages}
              total={users.data.total}
              onChange={goToPage}
            />
          ) : null}
        </div>

        {inspecting ? (
          <UserInspectPane id={inspecting} onClose={() => setInspecting(null)} />
        ) : (
          <aside className="hidden rounded-(--radius-card) border border-dashed border-line px-4 py-10 text-center text-sm text-muted xl:block">
            Baxmaq üçün siyahıdan istifadəçi seçin.
          </aside>
        )}
      </div>

      {pending ? (
        <ConfirmDialog
          title={pending.grant ? 'Moderator et' : 'Moderatorluğu götür'}
          description={
            pending.grant
              ? `${pending.target.fullName} elan moderasiyası və şikayətlərə giriş əldə edəcək. Dəyişiklik audit edilir.`
              : `${pending.target.fullName} moderasiya bölmələrinə girişi itirəcək. Dəyişiklik audit edilir.`
          }
          confirmLabel={pending.grant ? 'Moderator et' : 'Götür'}
          busy={role.isPending}
          onConfirm={() => role.run({ id: pending.target.id, grant: pending.grant })}
          onCancel={() => setPending(null)}
        />
      ) : null}
    </div>
  )
}

/**
 * The inspect pane — reading only (PD-7.6).
 *
 * There is deliberately nothing here that changes an account: no ban, no suspend, no delete, no
 * force-logout, no profile edit. The only mutation on this screen is the Moderator role, and it
 * lives in the table where the confirmation belongs.
 */
function UserInspectPane({ id, onClose }: { id: string; onClose: () => void }) {
  const user = useQuery({ queryKey: adminKeys.user(id), queryFn: () => getUser(id), retry: false })

  if (user.isPending) {
    return <Skeleton className="h-64" />
  }

  if (user.isError || !user.data) {
    return <ErrorState description="İstifadəçini yükləmək mümkün olmadı." onRetry={() => void user.refetch()} />
  }

  const data = user.data

  return (
    <aside className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <h2 className="text-base font-semibold text-ink">{data.fullName}</h2>

        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Bağla
        </Button>
      </div>

      <dl className="divide-y divide-line rounded-(--radius-input) border border-line text-sm">
        <InspectRow label="Nömrə" value={data.phoneNumber} />
        <InspectRow label="E-mail" value={data.email ?? '—'} />
        <InspectRow label="Rol" value={data.role} />
        <InspectRow label="Nömrə təsdiqi" value={data.isPhoneVerified ? 'Təsdiqlənib' : 'Təsdiqlənməyib'} />
        <InspectRow label="Qeydiyyat" value={formatDate(data.createdAt)} />
      </dl>

      <p className="text-xs text-muted">
        Bu bölmə yalnız baxış üçündür. Hesab bloklama, silmə və ya profil redaktəsi mövcud deyil.
      </p>
    </aside>
  )
}

function InspectRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-3 py-2">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right text-ink">{value}</dd>
    </div>
  )
}
