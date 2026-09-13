import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'

import { ApiError } from '@/api/client'
import { Button } from '@/components/ui/Button'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import { testIds } from '@/testIds'

import { createPromotionOrder, getPromotionPackages, promotionKeys } from './api'

interface PromotePackageDialogProps {
  listingId: string
  onClose: () => void
}

/**
 * What the seller is told before paying. Tap.az's rule, adopted as OvcuPirim's: a paid promotion is
 * not refunded when the listing is later removed, sold, or blocked for breaking the rules.
 */
const POLICY = 'Elan silinsə, satılsa və ya qaydaları pozduğuna görə bloklansa, ödəniş geri qaytarılmır.'

/**
 * The server says exactly why a purchase cannot start ("Bu elan artıq irəli çəkilib.",
 * "Yalnız aktiv elan irəli çəkilə bilər."); anything else gets the generic line.
 */
function purchaseErrorMessage(error: unknown): string {
  if (error instanceof ApiError && (error.status === 409 || error.status === 400) && error.problem?.detail) {
    return error.problem.detail
  }

  return 'Sifariş yaradıla bilmədi. Yenidən cəhd edin.'
}

/**
 * Package choice, then a real browser navigation to the gateway's hosted checkout — never an
 * in-page "success" of our own. What actually happened is decided server-side and shown on
 * PromotionReturnPage after the redirect back.
 */
export function PromotePackageDialog({ listingId, onClose }: PromotePackageDialogProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    if (ref.current && !ref.current.open) {
      ref.current.showModal()
    }
  }, [])

  const packages = useQuery({
    queryKey: promotionKeys.packages,
    queryFn: getPromotionPackages,
  })

  const [selected, setSelected] = useState<number | null>(null)

  const buy = useMutation({
    mutationFn: (packageId: number) => createPromotionOrder(listingId, packageId),
    onSuccess: (result) => {
      // A real navigation, not a client-side route change: the destination is the gateway's own
      // hosted checkout page (or, in Development, the simulated one), off this origin.
      window.location.assign(result.redirectUrl)
    },
  })

  return (
    <dialog
      ref={ref}
      aria-label="Elanı irəli çək"
      onClose={onClose}
      onCancel={onClose}
      data-testid={testIds.promotionDialog}
      className="w-[min(28rem,calc(100vw-2rem))] rounded-(--radius-card) border border-line bg-surface p-0 text-ink backdrop:bg-black/40"
    >
      <div className="flex flex-col gap-4 p-5">
        <h2 className="text-base font-semibold text-ink">Elanı irəli çək</h2>

        {packages.isPending ? (
          <div className="flex flex-col gap-2" aria-busy="true">
            <Skeleton className="h-16" />
            <Skeleton className="h-16" />
          </div>
        ) : packages.isError ? (
          <ErrorState description="Paketləri yükləmək mümkün olmadı." onRetry={() => void packages.refetch()} />
        ) : packages.data.length === 0 ? (
          <p className="text-sm text-muted">Hazırda satış üçün paket yoxdur.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {packages.data.map((pkg) => (
              <li key={pkg.id}>
                <label
                  className={`flex cursor-pointer items-start justify-between gap-3 rounded-(--radius-input) border p-3 transition-colors ${
                    selected === pkg.id ? 'border-interactive bg-interactive-soft' : 'border-line'
                  }`}
                >
                  <span className="flex items-start gap-3">
                    <input
                      type="radio"
                      name="promotion-package"
                      checked={selected === pkg.id}
                      onChange={() => setSelected(pkg.id)}
                      className="mt-1"
                      data-testid={testIds.promotionPackageOption}
                      value={pkg.id}
                    />
                    <span>
                      <span className="block font-semibold text-ink">{pkg.nameAz}</span>
                      <span className="block text-sm text-muted">
                        {pkg.durationDays} gün · hər {pkg.bumpIntervalHours} saatdan bir irəli çəkilir
                      </span>
                      {pkg.descriptionAz ? <span className="block text-xs text-faint">{pkg.descriptionAz}</span> : null}
                    </span>
                  </span>
                  <span className="whitespace-nowrap font-semibold text-ink">
                    {pkg.priceAzn.toFixed(2)} {pkg.currency}
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}

        <p className="text-xs text-muted">{POLICY}</p>

        {buy.isError ? (
          <p role="alert" className="text-sm text-accent">
            {purchaseErrorMessage(buy.error)}
          </p>
        ) : null}

        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose} disabled={buy.isPending}>
            İmtina
          </Button>
          <Button
            type="button"
            size="sm"
            disabled={selected === null || buy.isPending}
            onClick={() => selected !== null && buy.mutate(selected)}
          >
            {buy.isPending ? 'Yönləndirilir…' : 'Ödənişə keç'}
          </Button>
        </div>
      </div>
    </dialog>
  )
}
