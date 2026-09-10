import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { Input } from '@/components/ui/Input'
import { Select } from '@/components/ui/Select'
import { Skeleton } from '@/components/ui/Skeleton'
import { Textarea } from '@/components/ui/Textarea'
import { adminKeys, getPaymentOrder, getPaymentOrders, refundPaymentOrder } from '@/features/admin/api'
import { QueueFilters, QueuePagination, StatusBadge } from '@/features/admin/components/QueueChrome'
import { QueueTable } from '@/features/admin/components/QueueTable'
import type { QueueColumn } from '@/features/admin/components/QueueTable'
import type { AdminPaymentOrder, PaymentTransaction } from '@/features/admin/types'
import { useAdminAction } from '@/features/admin/useAdminAction'
import { useAdminQueue } from '@/features/admin/useAdminQueue'
import { formatDate, formatNumber } from '@/features/listings/format'

/** The statuses money can still come back from — the server enforces the same list. */
const REFUNDABLE = new Set(['Paid', 'PartiallyRefunded', 'PaidAfterExpiry'])

function money(amount: number, currency: string): string {
  return `${formatNumber(amount)} ${currency === 'AZN' ? '₼' : currency}`
}

/** Ledger wording an operator can act on, one line per event type the API records. */
const eventLabels: Record<string, string> = {
  OrderCreated: 'Sifariş yaradıldı',
  CallbackReceived: 'Callback qəbul edildi',
  StatusChecked: 'Status yoxlanıldı',
  RefundRequested: 'Geri qaytarma tələb edildi',
  RefundConfirmed: 'Geri qaytarma təsdiqləndi',
  VerificationFailed: 'Doğrulama uğursuz oldu',
}

interface RefundDialogProps {
  order: AdminPaymentOrder
  error?: string | undefined
  busy: boolean
  onConfirm: (amount: number | null, reason: string) => void
  onCancel: () => void
}

/**
 * Full refund unless an amount is typed; the server validates a partial amount against what is
 * actually left, never against the original total. Either kind reverses the promotion for good.
 */
function RefundDialog({ order, error, busy, onConfirm, onCancel }: RefundDialogProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const remaining = order.amountAzn - order.refundedAmountAzn

  useEffect(() => {
    if (ref.current && !ref.current.open) {
      ref.current.showModal()
    }
  }, [])

  const parsed = amount.trim() === '' ? null : Number(amount.replace(',', '.'))
  const amountInvalid = parsed !== null && (!Number.isFinite(parsed) || parsed <= 0 || parsed > remaining)

  return (
    <dialog
      ref={ref}
      aria-label="Ödənişi geri qaytar"
      onClose={onCancel}
      onCancel={onCancel}
      className="w-[min(28rem,calc(100vw-2rem))] rounded-(--radius-card) border border-line bg-surface p-0 text-ink backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-4 p-5">
        <h2 className="text-base font-semibold text-ink">Ödənişi geri qaytar</h2>
        <p className="text-sm leading-relaxed text-muted">
          Sifariş № {order.listingShortId} · {order.packageNameAz}. Qalıq: {money(remaining, order.currency)}. Geri qaytarma —
          tam və ya qismən — irəli çəkməni dərhal və birdəfəlik dayandırır.
        </p>

        <Input
          label="Məbləğ (boş = tam)"
          inputMode="decimal"
          value={amount}
          onChange={(event) => setAmount(event.target.value)}
          error={amountInvalid ? `Məbləğ 0-dan böyük və ${formatNumber(remaining)}-dən çox olmamalıdır.` : undefined}
        />

        <Textarea
          label="Səbəb *"
          value={reason}
          maxLength={500}
          counter
          onChange={(event) => setReason(event.target.value)}
          error={error}
          required
        />

        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onCancel} disabled={busy}>
            İmtina
          </Button>
          <Button
            type="button"
            size="sm"
            onClick={() => onConfirm(parsed, reason.trim())}
            disabled={busy || amountInvalid || reason.trim().length === 0}
          >
            {busy ? 'Göndərilir…' : 'Geri qaytar'}
          </Button>
        </div>
      </div>
    </dialog>
  )
}

function LedgerTable({ transactions }: { transactions: PaymentTransaction[] }) {
  if (transactions.length === 0) {
    return <p className="text-sm text-muted">Hələ heç bir əməliyyat qeydə alınmayıb.</p>
  }

  return (
    <div className="overflow-x-auto rounded-(--radius-input) border border-line">
      <table className="w-full min-w-[32rem] border-collapse text-sm">
        <thead>
          <tr className="border-b border-line text-left text-xs font-semibold uppercase tracking-wide text-muted">
            <th scope="col" className="px-3 py-2">Vaxt</th>
            <th scope="col" className="px-3 py-2">Hadisə</th>
            <th scope="col" className="px-3 py-2">Gateway statusu</th>
            <th scope="col" className="px-3 py-2 text-right">Məbləğ</th>
            <th scope="col" className="px-3 py-2">İstinad</th>
          </tr>
        </thead>
        <tbody>
          {transactions.map((entry) => (
            <tr key={entry.id} className="border-b border-line/70 last:border-0">
              <td className="px-3 py-2 whitespace-nowrap text-muted">{new Date(entry.createdAt).toLocaleString('en-GB')}</td>
              <td className="px-3 py-2 text-ink">{eventLabels[entry.eventType] ?? entry.eventType}</td>
              <td className="px-3 py-2 text-muted">{entry.providerStatusRaw ?? '—'}</td>
              <td className="px-3 py-2 text-right text-ink">{entry.amountAzn === null ? '—' : formatNumber(entry.amountAzn)}</td>
              <td className="px-3 py-2 font-mono text-xs text-muted">{entry.providerReference ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

interface DetailPaneProps {
  orderId: string
  onClose: () => void
  onRefund: (order: AdminPaymentOrder) => void
}

/** One order, the promotion it funded, and its whole ledger — what a dispute is settled from. */
function DetailPane({ orderId, onClose, onRefund }: DetailPaneProps) {
  const detail = useQuery({
    queryKey: adminKeys.paymentOrder(orderId),
    queryFn: () => getPaymentOrder(orderId),
  })

  return (
    <section
      aria-label="Sifariş detalları"
      className="flex flex-col gap-4 rounded-(--radius-card) border border-line bg-surface p-4"
    >
      {detail.isPending ? (
        <div className="flex flex-col gap-2" aria-busy="true">
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-24" />
        </div>
      ) : detail.isError || !detail.data ? (
        <ErrorState description="Sifarişi yükləmək mümkün olmadı." onRetry={() => void detail.refetch()} />
      ) : (
        <>
          <header className="flex flex-wrap items-start justify-between gap-3">
            <div className="flex flex-col gap-1">
              <h2 className="text-base font-semibold text-ink">
                Sifariş № {detail.data.order.listingShortId} · {detail.data.order.packageNameAz}
              </h2>
              <p className="text-sm text-muted">
                {detail.data.order.listingTitle} · {detail.data.order.sellerName} · {detail.data.order.sellerPhone}
              </p>
              <p className="flex flex-wrap items-center gap-2 text-sm">
                <StatusBadge status={detail.data.order.status} />
                {detail.data.promotion ? (
                  <span className="flex items-center gap-1 text-muted">
                    Promosiya: <StatusBadge status={detail.data.promotion.status} />
                    {detail.data.promotion.expiresAt ? ` ${formatDate(detail.data.promotion.expiresAt)}-dək` : null}
                  </span>
                ) : null}
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              {REFUNDABLE.has(detail.data.order.status) ? (
                <Button type="button" size="sm" onClick={() => onRefund(detail.data.order)}>
                  Geri qaytar
                </Button>
              ) : null}
              <Button type="button" variant="secondary" size="sm" onClick={onClose}>
                Bağla
              </Button>
            </div>
          </header>

          <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Məbləğ</dt>
              <dd className="text-ink">{money(detail.data.order.amountAzn, detail.data.order.currency)}</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Geri qaytarılıb</dt>
              <dd className="text-ink">{money(detail.data.order.refundedAmountAzn, detail.data.order.currency)}</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Müddət</dt>
              <dd className="text-ink">{detail.data.order.durationDays} gün</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Gateway istinadı</dt>
              <dd className="font-mono text-xs text-ink">{detail.data.order.providerOrderReference ?? '—'}</dd>
            </div>
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Yaradılıb</dt>
              <dd className="text-ink">{new Date(detail.data.order.createdAt).toLocaleString('en-GB')}</dd>
            </div>
            {detail.data.promotion?.reversedReason ? (
              <div className="flex justify-between gap-3 sm:col-span-2">
                <dt className="text-muted">Ləğv səbəbi</dt>
                <dd className="text-ink">{detail.data.promotion.reversedReason}</dd>
              </div>
            ) : null}
          </dl>

          <div className="flex flex-col gap-2">
            <h3 className="text-sm font-semibold text-ink">Əməliyyat tarixçəsi</h3>
            <LedgerTable transactions={detail.data.transactions} />
          </div>
        </>
      )}
    </section>
  )
}

/**
 * Payments — the reconciliation view (integration audit M-3). Every order, searchable by listing
 * number, gateway reference, seller name or phone; one click opens the ledger; refunds go through
 * the same server rule everything else does (money that moved is refundable, and a refund always
 * reverses the promotion).
 */
export function AdminPaymentsPage() {
  const { values, update, page, goToPage } = useAdminQueue()
  const [inspecting, setInspecting] = useState<string | null>(null)
  const [refunding, setRefunding] = useState<AdminPaymentOrder | null>(null)

  const orders = useQuery({
    queryKey: adminKeys.paymentOrders(values),
    queryFn: () => getPaymentOrders(values),
  })

  const refund = useAdminAction<{ id: string; amount: number | null; reason: string }>({
    action: ({ id, amount, reason }) => refundPaymentOrder(id, amount, reason),
    invalidate: [
      adminKeys.paymentOrders(values),
      ...(refunding ? [adminKeys.paymentOrder(refunding.id)] : []),
      adminKeys.overview,
    ],
    onSuccess: () => setRefunding(null),
  })

  const columns: QueueColumn<AdminPaymentOrder>[] = [
    {
      key: 'listing',
      header: 'Elan',
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-ink">№ {row.listingShortId}</span>
          <span className="text-xs text-muted">{row.listingTitle}</span>
        </div>
      ),
    },
    {
      key: 'seller',
      header: 'Satıcı',
      secondary: true,
      render: (row) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-ink">{row.sellerName}</span>
          <span className="text-xs text-muted">{row.sellerPhone}</span>
        </div>
      ),
    },
    {
      key: 'package',
      header: 'Paket',
      secondary: true,
      render: (row) => (
        <span className="text-ink">
          {row.packageNameAz} · {row.durationDays} gün
        </span>
      ),
    },
    {
      key: 'amount',
      header: 'Məbləğ',
      align: 'right',
      render: (row) => (
        <div className="flex flex-col items-end gap-0.5">
          <span className="text-ink">{money(row.amountAzn, row.currency)}</span>
          {row.refundedAmountAzn > 0 ? (
            <span className="text-xs text-muted">−{money(row.refundedAmountAzn, row.currency)}</span>
          ) : null}
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <div className="flex flex-wrap gap-1">
          <StatusBadge status={row.status} />
          {row.promotionStatus === 'Active' ? <Badge tone="new">Aktiv promosiya</Badge> : null}
        </div>
      ),
    },
    {
      key: 'created',
      header: 'Tarix',
      align: 'right',
      render: (row) => <span className="text-muted">{formatDate(row.createdAt)}</span>,
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold text-ink">Ödənişlər</h1>
        <p className="text-sm text-muted">
          İrəli çəkmə sifarişləri, gateway tarixçəsi və geri qaytarmalar. Refund gözləyən gec ödənişlər üçün statusu
          "Gec ödəniş" seçin.
        </p>
      </header>

      <QueueFilters>
        <Select label="Status" value={values['status'] ?? ''} onChange={(event) => update({ status: event.target.value })}>
          <option value="">Hamısı</option>
          <option value="AwaitingPayment">Ödəniş gözlənilir</option>
          <option value="Paid">Ödənilib</option>
          <option value="PaidAfterExpiry">Gec ödəniş — refund gözləyir</option>
          <option value="PartiallyRefunded">Qismən geri qaytarılıb</option>
          <option value="Refunded">Geri qaytarılıb</option>
          <option value="Failed">Uğursuz</option>
          <option value="Expired">Vaxtı bitib</option>
        </Select>

        <form
          onSubmit={(event) => {
            event.preventDefault()
            const value = String(new FormData(event.currentTarget).get('q') ?? '')
            update({ q: value.trim() === '' ? null : value.trim() })
          }}
        >
          <Input label="Axtarış" name="q" defaultValue={values['q'] ?? ''} placeholder="Elan №, istinad, ad və ya nömrə" />
        </form>
      </QueueFilters>

      {refund.state.kind === 'conflict' ? (
        <p role="status" className="rounded-(--radius-input) border border-line bg-surface px-3 py-2 text-sm text-ink">
          {refund.state.message} Siyahı yeniləndi.
        </p>
      ) : null}

      {refund.state.kind === 'error' ? (
        <p role="alert" className="text-sm text-accent">
          {refund.state.message}
        </p>
      ) : null}

      {refund.state.kind === 'done' ? (
        <p role="status" className="text-sm text-interactive">
          Geri qaytarma icra edildi; promosiya dayandırıldı.
        </p>
      ) : null}

      <QueueTable
        columns={columns}
        rows={orders.data?.items}
        rowKey={(row) => row.id}
        isPending={orders.isPending}
        isError={orders.isError}
        onRetry={() => void orders.refetch()}
        emptyTitle="Bu şərtlərə uyğun sifariş yoxdur."
        errorDescription="Sifarişləri yükləmək mümkün olmadı."
        selectedKey={inspecting ?? undefined}
        onSelect={(row) => setInspecting(row.id)}
      />

      {orders.data ? (
        <QueuePagination page={page} totalPages={orders.data.totalPages} total={orders.data.total} onChange={goToPage} />
      ) : null}

      {inspecting ? (
        <DetailPane orderId={inspecting} onClose={() => setInspecting(null)} onRefund={(order) => setRefunding(order)} />
      ) : null}

      {refunding ? (
        <RefundDialog
          order={refunding}
          error={refund.fieldError}
          busy={refund.isPending}
          onConfirm={(amount, reason) => refund.run({ id: refunding.id, amount, reason })}
          onCancel={() => {
            refund.reset()
            setRefunding(null)
          }}
        />
      ) : null}

      <p className="text-xs text-faint">
        Paketlərin özü <Link to="/admin/packages" className="text-interactive hover:underline">Paketlər</Link> bölməsində
        idarə olunur.
      </p>
    </div>
  )
}
