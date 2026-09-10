import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'

import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { getMyPaymentOrder, promotionKeys } from '@/features/promotions/api'
import type { PaymentOrderStatus } from '@/features/promotions/types'
import { testIds } from '@/testIds'

const PENDING_STATUSES: PaymentOrderStatus[] = ['Created', 'AwaitingPayment']

const STATUS_COPY: Record<PaymentOrderStatus, { title: string; description: string }> = {
  Created: { title: 'Ödəniş yoxlanılır…', description: 'Nəticəni gözləyirik.' },
  AwaitingPayment: { title: 'Ödəniş yoxlanılır…', description: 'Nəticəni gözləyirik.' },
  Paid: { title: 'Elanınız irəli çəkildi', description: 'Ödəniş təsdiqləndi və irəli çəkmə aktivləşdi.' },
  Failed: { title: 'Ödəniş uğursuz oldu', description: 'Kartınızdan məbləğ tutulmayıb. Yenidən cəhd edə bilərsiniz.' },
  Expired: { title: 'Sifarişin vaxtı bitib', description: 'Bu sifariş üçün ödəniş müddəti keçib. Yenidən sifariş yaradın.' },
  Canceled: { title: 'Sifariş ləğv edildi', description: 'Bu sifariş ləğv olunub.' },
  Refunded: { title: 'Ödəniş geri qaytarılıb', description: 'İrəli çəkmə dayandırılıb.' },
  PartiallyRefunded: { title: 'Ödəniş qismən geri qaytarılıb', description: 'İrəli çəkmə dayandırılıb.' },
  // The gateway confirmed the capture only after the order's window closed. The approved rule is
  // that such an order never activates a promotion, so the honest outcome is a refund, not a bump.
  PaidAfterExpiry: {
    title: 'Ödəniş gec təsdiqləndi',
    description:
      'Sifarişin vaxtı bitdikdən sonra ödəniş təsdiqləndiyi üçün irəli çəkmə aktivləşmədi. Məbləğ geri qaytarılacaq.',
  },
}

/**
 * Where the gateway sends the browser back to after checkout. The query string it arrives with
 * (`?outcome=success|error`) is never read as the verdict — only the server's own verified order
 * status is. A callback can legitimately arrive after this redirect, so the order is polled for a
 * few seconds rather than read once.
 */
export function PromotionReturnPage() {
  const { id } = useParams<{ id: string }>()

  const order = useQuery({
    queryKey: promotionKeys.order(id ?? ''),
    queryFn: () => getMyPaymentOrder(id!),
    enabled: Boolean(id),
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status && PENDING_STATUSES.includes(status) ? 1500 : false
    },
  })

  if (!id) {
    return <ErrorState description="Sifariş tapılmadı." />
  }

  if (order.isPending) {
    return (
      <div className="mx-auto max-w-md py-14" aria-busy="true">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="mt-4 h-20 w-full" />
      </div>
    )
  }

  if (order.isError) {
    return <ErrorState description="Sifarişin vəziyyətini yükləmək mümkün olmadı." onRetry={() => void order.refetch()} />
  }

  const copy = STATUS_COPY[order.data.status]

  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 py-14 text-center">
      <h1 className="text-xl font-semibold text-ink" data-testid={testIds.promotionReturnStatus} data-status={order.data.status}>
        {copy.title}
      </h1>
      <p className="text-sm text-muted">{copy.description}</p>

      <Link to="/kabinet/elanlarim" className="text-sm font-semibold text-interactive hover:underline">
        Mənim elanlarıma qayıt
      </Link>
    </div>
  )
}
