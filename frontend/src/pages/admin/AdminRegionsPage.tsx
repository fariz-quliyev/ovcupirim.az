import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Select'
import { Textarea } from '@/components/ui/Textarea'
import { adminKeys, getAdminRegions, importRegions } from '@/features/admin/api'
import { ConfirmDialog } from '@/features/admin/components/ActionDialog'
import { QueueFilters } from '@/features/admin/components/QueueChrome'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminRegion, RegionImportResult } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { DesktopOnlyNotice } from '@/layouts/AdminLayout'

/**
 * Region administration and the authoritative-dataset import.
 *
 * The import endpoint is idempotent — an existing slug is updated rather than duplicated — and
 * every row must state `isSelectable` explicitly. This screen refuses a payload that omits it
 * rather than defaulting on the operator's behalf, because whether a place can hold a listing is
 * not a guess the UI gets to make.
 */
export function AdminRegionsPage() {
  const { values, update } = useAdminQueue()
  const [payload, setPayload] = useState('')
  const [parseError, setParseError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<unknown[] | null>(null)
  const [result, setResult] = useState<RegionImportResult | null>(null)

  const regions = useQuery({ queryKey: adminKeys.regions, queryFn: getAdminRegions })

  const runImport = useAdminAction<unknown[]>({
    action: (rows) => importRegions(rows).then((r) => setResult(r)),
    invalidate: [adminKeys.regions, ['catalog']],
    onSuccess: () => {
      setConfirming(null)
      setPayload('')
    },
  })

  function prepare() {
    setParseError(null)
    setResult(null)

    try {
      const parsed: unknown = JSON.parse(payload)
      const rows = Array.isArray(parsed) ? parsed : (parsed as { regions?: unknown[] }).regions

      if (!Array.isArray(rows) || rows.length === 0) {
        setParseError('Massiv və ya {"regions": [...]} formatı gözlənilir.')
        return
      }

      const missing = rows.filter(
        (row) => typeof row !== 'object' || row === null || !('isSelectable' in row),
      )

      if (missing.length > 0) {
        setParseError(`${missing.length} sətirdə "isSelectable" göstərilməyib. Hər sətir bunu açıq bildirməlidir.`)
        return
      }

      setConfirming(rows)
    } catch {
      setParseError('JSON oxunmadı.')
    }
  }

  const typeFilter = values['type'] ?? ''
  const filtered = (regions.data ?? []).filter((row) => typeFilter === '' || row.type === typeFilter)

  const columns: QueueColumn<AdminRegion>[] = [
    {
      key: 'name',
      header: 'Ad',
      render: (row) => (
        <div className="flex flex-col gap-0.5" style={{ paddingLeft: `${row.depth}rem` }}>
          <span className="text-ink">{row.nameAz}</span>
          <span className="text-xs text-muted">/{row.slug}</span>
        </div>
      ),
    },
    { key: 'type', header: 'Növ', render: (row) => row.type },
    {
      key: 'flags',
      header: 'Vəziyyət',
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          {row.isActive ? <Badge tone="new">Aktiv</Badge> : <Badge>Deaktiv</Badge>}
          {row.isSelectable ? <Badge tone="neutral">Seçilə bilər</Badge> : null}
        </div>
      ),
    },
    { key: 'order', header: 'Sıra', secondary: true, align: 'right', render: (row) => row.sortOrder },
  ]

  return (
    <DesktopOnlyNotice>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <h1 className="text-xl font-semibold text-ink">Regionlar</h1>
          <p className="text-sm text-muted">
            Tam inzibati siyahı — ictimai seçicidə görünməyən qeydlər də daxil olmaqla.
          </p>
        </header>

        <QueueFilters>
          <Select label="Növ" value={typeFilter} onChange={(event) => update({ type: event.target.value })}>
            <option value="">Hamısı</option>
            <option value="City">Şəhər</option>
            <option value="District">Rayon</option>
            <option value="Settlement">Qəsəbə</option>
          </Select>
        </QueueFilters>

        <QueueTable
          columns={columns}
          rows={filtered}
          rowKey={(row) => String(row.id)}
          isPending={regions.isPending}
          isError={regions.isError}
          onRetry={() => void regions.refetch()}
          emptyTitle="Region tapılmadı."
          errorDescription="Regionları yükləmək mümkün olmadı."
        />

        <section className="flex flex-col gap-3 rounded-(--radius-card) border border-line bg-surface p-4">
          <h2 className="text-base font-semibold text-ink">Məlumat bazasını yüklə</h2>
          <p className="text-sm text-muted">
            Eyni slug varsa yenilənir, təkrarlanmır. Hər sətirdə <code>isSelectable</code> açıq
            göstərilməlidir.
          </p>

          <Textarea
            label="JSON"
            value={payload}
            rows={8}
            onChange={(event) => setPayload(event.target.value)}
            error={parseError ?? undefined}
            placeholder='[{"slug":"baki","nameAz":"Bakı","type":"City","isSelectable":true,"isActive":true}]'
          />

          <div className="flex flex-wrap items-center gap-3">
            <Button type="button" size="sm" disabled={payload.trim() === ''} onClick={prepare}>
              Yoxla və yüklə
            </Button>

            {result ? (
              <p role="status" className="text-sm text-muted">
                {result.inserted} əlavə, {result.updated} yeniləndi, {result.skipped} ötürüldü.
              </p>
            ) : null}
          </div>

          {runImport.state.kind === 'error' ? (
            <p role="alert" className="text-sm text-accent">
              {runImport.state.message}
            </p>
          ) : null}
        </section>

        {confirming ? (
          <ConfirmDialog
            title="Regionları yüklə"
            description={`${confirming.length} sətir göndəriləcək. Mövcud slug-lar yenilənəcək; bu, saytdakı yer seçicisinə dərhal təsir edir.`}
            confirmLabel="Yüklə"
            busy={runImport.isPending}
            onConfirm={() => runImport.run(confirming)}
            onCancel={() => setConfirming(null)}
          />
        ) : null}
      </div>
    </DesktopOnlyNotice>
  )
}
