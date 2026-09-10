import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { adminKeys, getOverview } from '@/features/admin/api'
import { waitingFor } from '@/features/admin/format'
import type { QueueDepth } from '@/features/admin/types'
import { formatNumber } from '@/features/listings/format'

interface QueueCardProps {
  title: string
  depth: QueueDepth
  to: string
  /** Shown instead of a link when the operator's role cannot open that queue. */
  disabled?: boolean
}

function QueueCard({ title, depth, to, disabled = false }: QueueCardProps) {
  const body = (
    <div className="flex flex-col gap-1 rounded-(--radius-card) border border-line bg-surface p-4 transition-colors hover:border-interactive">
      <p className="text-sm text-muted">{title}</p>
      <p className="text-3xl font-semibold text-ink">{formatNumber(depth.count)}</p>
      <p className="text-xs text-faint">
        {depth.count === 0 ? 'Növbə boşdur' : `Ən köhnəsi: ${waitingFor(depth.oldestWaitingSince)}`}
      </p>
    </div>
  )

  return disabled ? body : <Link to={to}>{body}</Link>
}

function DecisionRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-4 py-2">
      <dt className="text-sm text-muted">{label}</dt>
      <dd className="text-[15px] font-medium text-ink">{formatNumber(value)}</dd>
    </div>
  )
}

/**
 * The operations landing page. Deliberately answers one question — is the site being kept up with —
 * and carries no revenue, conversion or ranking figures (PD-7.3).
 */
export function AdminDashboardPage() {
  const overview = useQuery({ queryKey: adminKeys.overview, queryFn: getOverview })

  if (overview.isPending) {
    return (
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-busy="true">
        {Array.from({ length: 4 }, (_, index) => (
          <Skeleton key={index} className="h-28" />
        ))}
      </div>
    )
  }

  if (overview.isError || !overview.data) {
    return <ErrorState description="İcmalı yükləmək mümkün olmadı." onRetry={() => void overview.refetch()} />
  }

  const data = overview.data
  const recent = data.last24Hours

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">İcmal</h1>
        <p className="text-sm text-muted">Gözləyən işlər və son 24 saatın qərarları.</p>
      </header>

      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <QueueCard title="Gözləyən elanlar" depth={data.pendingListings} to="/admin/moderation" />
        <QueueCard
          title="Diqqət tələb edən elanlar"
          depth={data.strictPendingListings}
          to="/admin/moderation?strict=true"
        />
        <QueueCard title="Açıq şikayətlər" depth={data.openReports} to="/admin/reports" />
        <QueueCard title="Mağaza müraciətləri" depth={data.pendingStores} to="/admin/stores" />
        <QueueCard title="Refund gözləyən ödənişlər" depth={data.lateCaptures} to="/admin/payments?status=PaidAfterExpiry" />
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-(--radius-card) border border-line bg-surface">
          <h2 className="border-b border-line px-4 py-3 text-base font-semibold text-ink">
            Son 24 saat
          </h2>

          <dl className="divide-y divide-line">
            <DecisionRow label="Təsdiqlənmiş elanlar" value={recent.listingsApproved} />
            <DecisionRow label="Rədd edilmiş elanlar" value={recent.listingsRejected} />
            <DecisionRow label="Bloklanmış elanlar" value={recent.listingsBlocked} />
            <DecisionRow label="Həll edilmiş şikayətlər" value={recent.reportsResolved} />
            <DecisionRow label="Rədd edilmiş şikayətlər" value={recent.reportsDismissed} />
            <DecisionRow label="Təsdiqlənmiş mağazalar" value={recent.storesApproved} />
            <DecisionRow label="Rədd edilmiş mağaza müraciətləri" value={recent.storesRejected} />
          </dl>
        </section>

        <section className="rounded-(--radius-card) border border-line bg-surface">
          <h2 className="border-b border-line px-4 py-3 text-base font-semibold text-ink">Sistem vəziyyəti</h2>

          <dl className="divide-y divide-line">
            <div className="flex items-baseline justify-between gap-4 px-4 py-2">
              <dt className="text-sm text-muted">Verilənlər bazası</dt>
              <dd className={`text-[15px] font-medium ${data.databaseReachable ? 'text-interactive' : 'text-accent'}`}>
                {data.databaseReachable ? 'Əlçatandır' : 'Əlçatan deyil'}
              </dd>
            </div>

            <div className="flex items-baseline justify-between gap-4 px-4 py-2">
              <dt className="text-sm text-muted">Hesabat vaxtı</dt>
              <dd className="text-[15px] text-ink">{new Date(data.generatedAt).toLocaleTimeString('en-GB')}</dd>
            </div>
          </dl>
        </section>
      </div>
    </div>
  )
}
