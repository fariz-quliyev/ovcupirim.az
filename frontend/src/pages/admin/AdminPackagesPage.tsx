import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

import { ApiError } from '@/api/client'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Checkbox'
import { Input } from '@/components/ui/Input'
import { Textarea } from '@/components/ui/Textarea'
import {
  adminKeys,
  createPromotionPackage,
  getAdminPromotionPackages,
  updatePromotionPackage,
} from '@/features/admin/api'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminPromotionPackage } from '@/features/admin/types'
import { formatNumber } from '@/features/listings/format'

interface PackageDialogProps {
  /** Null creates; a package edits. The code is fixed after creation, like a category slug. */
  existing: AdminPromotionPackage | null
  onClose: () => void
}

/**
 * One form for both create and edit. Every field is sent every time — the update endpoint assigns
 * all of them, so a partial body would silently blank what it left out (the same contract the
 * category editor follows).
 */
function PackageDialog({ existing, onClose }: PackageDialogProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const queryClient = useQueryClient()

  const [code, setCode] = useState(existing?.code ?? '')
  const [nameAz, setNameAz] = useState(existing?.nameAz ?? '')
  const [descriptionAz, setDescriptionAz] = useState(existing?.descriptionAz ?? '')
  const [durationDays, setDurationDays] = useState(String(existing?.durationDays ?? 7))
  const [priceAzn, setPriceAzn] = useState(existing ? String(existing.priceAzn) : '')
  const [sortOrder, setSortOrder] = useState(String(existing?.sortOrder ?? 10))
  const [isActive, setIsActive] = useState(existing?.isActive ?? true)

  useEffect(() => {
    if (ref.current && !ref.current.open) {
      ref.current.showModal()
    }
  }, [])

  const save = useMutation({
    mutationFn: () => {
      const body = {
        nameAz: nameAz.trim(),
        descriptionAz: descriptionAz.trim() === '' ? null : descriptionAz.trim(),
        durationDays: Number(durationDays),
        priceAzn: Number(priceAzn.replace(',', '.')),
        sortOrder: Number(sortOrder),
      }

      return existing
        ? updatePromotionPackage(existing.id, { ...body, isActive })
        : createPromotionPackage({ ...body, code: code.trim() })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: adminKeys.promotionPackages })
      onClose()
    },
  })

  const apiError = save.error instanceof ApiError ? save.error : null
  const fieldError = (field: string) => apiError?.fieldError(field)

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    save.mutate()
  }

  return (
    <dialog
      ref={ref}
      aria-label={existing ? 'Paketi redaktə et' : 'Yeni paket'}
      onClose={onClose}
      onCancel={onClose}
      className="w-[min(32rem,calc(100vw-2rem))] rounded-(--radius-card) border border-line bg-surface p-0 text-ink backdrop:bg-black/40"
    >
      <form onSubmit={submit} className="flex flex-col gap-4 p-5">
        <h2 className="text-base font-semibold text-ink">{existing ? 'Paketi redaktə et' : 'Yeni paket'}</h2>

        {existing ? (
          <p className="text-sm text-muted">
            Kod: <span className="font-mono">{existing.code}</span> (dəyişdirilə bilməz)
          </p>
        ) : (
          <Input
            label="Kod *"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            placeholder="bump-7d"
            hint="Kiçik hərf, rəqəm və defis. Yaradıldıqdan sonra dəyişmir."
            error={fieldError('code')}
            required
          />
        )}

        <Input label="Ad *" value={nameAz} onChange={(event) => setNameAz(event.target.value)} error={fieldError('nameAz')} required />

        <Textarea
          label="Təsvir"
          value={descriptionAz}
          maxLength={500}
          counter
          onChange={(event) => setDescriptionAz(event.target.value)}
          error={fieldError('descriptionAz')}
        />

        <div className="grid gap-3 sm:grid-cols-3">
          <Input
            label="Müddət (gün) *"
            inputMode="numeric"
            value={durationDays}
            onChange={(event) => setDurationDays(event.target.value)}
            error={fieldError('durationDays')}
            required
          />
          <Input
            label="Qiymət (AZN) *"
            inputMode="decimal"
            value={priceAzn}
            onChange={(event) => setPriceAzn(event.target.value)}
            error={fieldError('priceAzn')}
            required
          />
          <Input
            label="Sıra *"
            inputMode="numeric"
            value={sortOrder}
            onChange={(event) => setSortOrder(event.target.value)}
            error={fieldError('sortOrder')}
            required
          />
        </div>

        {existing ? (
          <Checkbox
            label="Kataloqda aktivdir"
            checked={isActive}
            onChange={(event) => setIsActive(event.target.checked)}
            hint="Söndürmək paketi satışdan çıxarır; artıq alınmış promosiyalara təsir etmir."
          />
        ) : null}

        {apiError && !apiError.problem?.errors ? (
          <p role="alert" className="text-sm text-accent">
            {apiError.problem?.detail ?? apiError.message}
          </p>
        ) : null}

        {save.isError && !apiError ? (
          <p role="alert" className="text-sm text-accent">
            Şəbəkə xətası. Yenidən cəhd edin.
          </p>
        ) : null}

        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose} disabled={save.isPending}>
            İmtina
          </Button>
          <Button type="submit" size="sm" disabled={save.isPending}>
            {save.isPending ? 'Göndərilir…' : existing ? 'Yadda saxla' : 'Yarat'}
          </Button>
        </div>
      </form>
    </dialog>
  )
}

/**
 * The promotion catalog (integration audit M-3). Nothing is ever deleted — a package that funded
 * an order is referenced by it forever — so retiring means switching it off.
 */
export function AdminPackagesPage() {
  const [editing, setEditing] = useState<AdminPromotionPackage | null | 'new'>(null)

  const packages = useQuery({
    queryKey: adminKeys.promotionPackages,
    queryFn: getAdminPromotionPackages,
  })

  const columns: QueueColumn<AdminPromotionPackage>[] = [
    {
      key: 'name',
      header: 'Paket',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-ink">{row.nameAz}</span>
          <span className="font-mono text-xs text-muted">{row.code}</span>
        </div>
      ),
    },
    { key: 'duration', header: 'Müddət', align: 'right', render: (row) => `${row.durationDays} gün` },
    {
      key: 'price',
      header: 'Qiymət',
      align: 'right',
      render: (row) => `${formatNumber(row.priceAzn)} ${row.currency === 'AZN' ? '₼' : row.currency}`,
    },
    { key: 'sort', header: 'Sıra', secondary: true, align: 'right', render: (row) => row.sortOrder },
    {
      key: 'active',
      header: 'Status',
      render: (row) => (row.isActive ? <Badge tone="new">Aktiv</Badge> : <Badge tone="neutral">Söndürülüb</Badge>),
    },
    {
      key: 'actions',
      header: 'Əməliyyat',
      align: 'right',
      render: (row) => (
        <Button type="button" size="sm" variant="secondary" onClick={() => setEditing(row)}>
          Redaktə et
        </Button>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-xl font-semibold text-ink">Paketlər</h1>
          <p className="text-sm text-muted">
            Satıcıya təklif olunan irəli çəkmə paketləri. Hər paket müddəti boyunca elanı hər 8 saatdan bir irəli
            çəkir; qiymət və müddət sifariş anında dondurulur.
          </p>
        </div>
        <Button type="button" size="sm" onClick={() => setEditing('new')}>
          Yeni paket
        </Button>
      </header>

      <QueueTable
        columns={columns}
        rows={packages.data}
        rowKey={(row) => String(row.id)}
        isPending={packages.isPending}
        isError={packages.isError}
        onRetry={() => void packages.refetch()}
        emptyTitle="Hələ paket yoxdur."
        emptyDescription="İlk paketi yaradın; satıcılar onu dərhal görəcək."
        errorDescription="Paketləri yükləmək mümkün olmadı."
      />

      {editing !== null ? (
        <PackageDialog existing={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      ) : null}
    </div>
  )
}
