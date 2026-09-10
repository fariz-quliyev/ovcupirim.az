import { useQuery } from '@tanstack/react-query'

import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { adminKeys, getAudit } from '@/features/admin/api'
import { AuditPayloadView, AuditRowSummary } from '@/features/admin/components/AuditTrail'
import { QueueFilters, QueuePagination } from '@/features/admin/components/QueueChrome'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AuditEntry } from '@/features/admin/types'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { DesktopOnlyNotice } from '@/layouts/AdminLayout'

/** The prefixes worth offering; the API matches any prefix, so this is a shortcut, not a whitelist. */
const actionGroups = [
  { value: '', label: 'Bütün əməliyyatlar' },
  { value: 'listing.moderation.', label: 'Elan moderasiyası' },
  { value: 'listing.screening.', label: 'Avtomatik yoxlama' },
  { value: 'store.', label: 'Mağaza qərarları' },
  { value: 'report.', label: 'Şikayətlər' },
  { value: 'user.', label: 'Rol dəyişiklikləri' },
  { value: 'Category', label: 'Kateqoriya dəyişiklikləri' },
]

/**
 * The audit trail, read-only.
 *
 * Admin-only (PD-7.4), and there is nothing here but reading: no edit, no delete, no bulk export.
 * The records behind it are append-only, and the UI does not pretend otherwise.
 */
export function AdminAuditPage() {
  const { values, update, page, goToPage, reset } = useAdminQueue()

  const audit = useQuery({
    queryKey: adminKeys.audit(values),
    queryFn: () => getAudit(values),
  })

  const columns: QueueColumn<AuditEntry>[] = [
    { key: 'action', header: 'Əməliyyat', render: (row) => <AuditRowSummary entry={row} /> },
    {
      key: 'entity',
      header: 'Obyekt',
      secondary: true,
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-ink">{row.entityType}</span>
          <span className="text-xs text-faint">{row.entityId}</span>
        </div>
      ),
    },
    { key: 'payload', header: 'Məlumat', render: (row) => <AuditPayloadView payload={row.payload} /> },
  ]

  return (
    <DesktopOnlyNotice>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <h1 className="text-xl font-semibold text-ink">Audit jurnalı</h1>
          <p className="text-sm text-muted">
            Yalnız oxunur. Qeydlər əlavə olunur, dəyişdirilmir və silinmir.
          </p>
        </header>

        <QueueFilters>
          <Select
            label="Əməliyyat"
            value={values['action'] ?? ''}
            onChange={(event) => update({ action: event.target.value })}
          >
            {actionGroups.map((group) => (
              <option key={group.value} value={group.value}>
                {group.label}
              </option>
            ))}
          </Select>

          <form
            onSubmit={(event) => {
              event.preventDefault()
              const form = new FormData(event.currentTarget)
              update({
                entityType: String(form.get('entityType') ?? '') || null,
                entityId: String(form.get('entityId') ?? '') || null,
              })
            }}
            className="flex flex-wrap items-end gap-3"
          >
            <Input label="Obyekt növü" name="entityType" defaultValue={values['entityType'] ?? ''} />
            <Input label="Obyekt ID" name="entityId" defaultValue={values['entityId'] ?? ''} />
            <Button type="submit" variant="secondary" size="sm">
              Süz
            </Button>
          </form>

          <Button type="button" variant="ghost" size="sm" onClick={reset}>
            Sıfırla
          </Button>
        </QueueFilters>

        <QueueTable
          columns={columns}
          rows={audit.data?.items}
          rowKey={(row) => row.id}
          isPending={audit.isPending}
          isError={audit.isError}
          onRetry={() => void audit.refetch()}
          emptyTitle="Qeyd tapılmadı."
          emptyDescription="Süzgəcləri dəyişib yenidən yoxlayın."
          errorDescription="Audit jurnalını yükləmək mümkün olmadı."
        />

        {audit.data ? (
          <QueuePagination
            page={page}
            totalPages={audit.data.totalPages}
            total={audit.data.total}
            onChange={goToPage}
          />
        ) : null}
      </div>
    </DesktopOnlyNotice>
  )
}
