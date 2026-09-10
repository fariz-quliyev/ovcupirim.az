import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { getMyPaymentOrders, promotionKeys } from '@/features/promotions/api'
import { paymentStatusLabels } from '@/features/promotions/format'
import type { PaymentOrder } from '@/features/promotions/types'
import { formatDate, formatNumber } from '@/features/listings/format'
import { testIds } from '@/testIds'

const PAID: PaymentOrder['status'][] = ['Paid', 'PartiallyRefunded', 'Refunded', 'PaidAfterExpiry']

/**
 * "Ödənişlərim": every promotion the seller ordered, with the state the server actually confirmed.
 * Nothing here is a receipt in the legal sense — the gateway's own statement is — but it is the one
 * place a seller can see what they bought and what happened to it.
 */
export function MyPaymentsPage() {
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? '1') || 1

  const orders = useQuery({
    queryKey: promotionKeys.myOrders(page),
    queryFn: () => getMyPaymentOrders(page),
  })

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold text-ink">Ödənişlərim</h1>
        <p className="text-sm text-muted">İrəli çəkmə sifarişləriniz və onların vəziyyəti.</p>
      </header>

      {orders.isPending ? (
        <div className="flex flex-col gap-2" aria-busy="true">
          <Skeleton className="h-16" />
          <Skeleton className="h-16" />
        </div>
      ) : orders.isError ? (
        <ErrorState description="Ödənişləri yükləmək mümkün olmadı." onRetry={() => void orders.refetch()} />
      ) : orders.data.items.length === 0 ? (
        <EmptyState
          title="Hələ ödəniş yoxdur."
          description='"Mənim elanlarım" səhifəsindən aktiv elanı irəli çəkə bilərsiniz.'
        />
      ) : (
        <ul className="flex flex-col gap-3">
          {orders.data.items.map((order) => (
            <li
              key={order.id}
              data-testid={testIds.myPaymentRow}
              className="flex flex-col gap-2 rounded-(--radius-card) border border-line bg-surface p-3 sm:flex-row sm:items-start sm:justify-between"
            >
              <div className="flex flex-col gap-1">
                <p className="font-semibold text-ink">{order.packageNameAz}</p>
                <p className="text-sm text-muted">
                  {order.durationDays} gün · {formatDate(order.createdAt)}
                </p>
                <p>
                  <Badge tone={PAID.includes(order.status) ? 'new' : order.status === 'AwaitingPayment' ? 'top' : 'neutral'}>
                    {paymentStatusLabels[order.status]}
                  </Badge>
                </p>
              </div>

              <div className="flex flex-col items-start gap-2 sm:items-end">
                <p className="text-[15px] font-semibold text-ink">
                  {formatNumber(order.amountAzn)} {order.currency === 'AZN' ? '₼' : order.currency}
                </p>
                <Link to={`/promotions/orders/${order.id}/return`} className="text-sm text-interactive hover:underline">
                  Nəticəyə bax
                </Link>
              </div>
            </li>
          ))}
        </ul>
      )}

      {orders.data && orders.data.totalPages > 1 ? (
        <nav aria-label="Səhifələr" className="flex items-center gap-3">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={page <= 1}
            onClick={() => setParams({ page: String(page - 1) })}
          >
            Əvvəlki
          </Button>
          <span className="text-sm text-muted">
            {page} / {orders.data.totalPages}
          </span>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={page >= orders.data.totalPages}
            onClick={() => setParams({ page: String(page + 1) })}
          >
            Növbəti
          </Button>
        </nav>
      ) : null}

      <p className="text-sm">
        <Link to="/kabinet/elanlarim" className="text-interactive hover:underline">
          Mənim elanlarıma qayıt
        </Link>
      </p>
    </div>
  )
}
