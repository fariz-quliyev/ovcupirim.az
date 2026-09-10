import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { Skeleton } from '@/components/ui/Skeleton'
import {
  deleteListing,
  getMyListings,
  listingKeys,
  markListingSold,
  restoreListing,
} from '@/features/listings/api'
import {
  bucketLabels,
  bucketOrder,
  formatDate,
  formatPrice,
  listingPath,
} from '@/features/listings/format'
import type { ListingBucket, ListingSummary } from '@/features/listings/types'
import { PromotePackageDialog } from '@/features/promotions/PromotePackageDialog'
import { testIds } from '@/testIds'

function isBucket(value: string | null): value is ListingBucket {
  return value !== null && (bucketOrder as string[]).includes(value)
}

/** "Mənim elanlarım": one tab per status, with the actions that status allows. */
export function MyListingsPage() {
  const [params, setParams] = useSearchParams()
  const queryClient = useQueryClient()
  const [promotingListingId, setPromotingListingId] = useState<string | null>(null)

  const raw = params.get('status')
  const bucket: ListingBucket = isBucket(raw) ? raw : 'active'

  const listings = useQuery({
    queryKey: listingKeys.mine(bucket),
    queryFn: () => getMyListings(bucket),
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['listings'] })
  }

  const remove = useMutation({ mutationFn: deleteListing, onSuccess: invalidate })
  const restore = useMutation({ mutationFn: (id: string) => restoreListing(id), onSuccess: invalidate })
  const sold = useMutation({ mutationFn: markListingSold, onSuccess: invalidate })

  const busy = remove.isPending || restore.isPending || sold.isPending

  return (
    <div className="flex flex-col gap-5">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold text-ink">Mənim elanlarım</h1>

        <Link to="/yeni-elan">
          <Button variant="accent">Yeni elan</Button>
        </Link>
      </header>

      <nav aria-label="Elan statusları" className="flex flex-wrap gap-2 border-b border-line pb-2">
        {bucketOrder.map((item) => (
          <button
            key={item}
            type="button"
            aria-current={item === bucket ? 'page' : undefined}
            onClick={() => setParams({ status: item })}
            className={`rounded-(--radius-button) px-3 py-1.5 text-sm font-medium transition-colors ${
              item === bucket ? 'bg-interactive text-white' : 'text-muted hover:text-ink'
            }`}
          >
            {bucketLabels[item]}
          </button>
        ))}
      </nav>

      {listings.isPending ? (
        <div className="flex flex-col gap-3" aria-busy="true">
          <Skeleton className="h-28" />
          <Skeleton className="h-28" />
        </div>
      ) : listings.isError ? (
        <ErrorState description="Elanları yükləmək mümkün olmadı." onRetry={() => void listings.refetch()} />
      ) : listings.data.items.length === 0 ? (
        <EmptyState
          title={`"${bucketLabels[bucket]}" bölməsində elan yoxdur.`}
          description="Yeni elan yerləşdirərək başlaya bilərsiniz."
        />
      ) : (
        <ul className="flex flex-col gap-3">
          {listings.data.items.map((listing) => (
            <ListingRow
              key={listing.id}
              listing={listing}
              busy={busy}
              onDelete={() => remove.mutate(listing.id)}
              onRestore={() => restore.mutate(listing.id)}
              onSold={() => sold.mutate(listing.id)}
              onPromote={() => setPromotingListingId(listing.id)}
            />
          ))}
        </ul>
      )}

      {promotingListingId ? (
        <PromotePackageDialog listingId={promotingListingId} onClose={() => setPromotingListingId(null)} />
      ) : null}
    </div>
  )
}

interface ListingRowProps {
  listing: ListingSummary
  busy: boolean
  onDelete: () => void
  onRestore: () => void
  onSold: () => void
  onPromote: () => void
}

function ListingRow({ listing, busy, onDelete, onRestore, onSold, onPromote }: ListingRowProps) {
  return (
    <li
      data-testid={testIds.myListingRow}
      className="flex flex-col gap-3 rounded-(--radius-card) border border-line bg-surface p-3 sm:flex-row sm:items-start"
    >
      {listing.primaryImageUrl ? (
        <img
          src={listing.primaryImageUrl}
          alt=""
          className="h-24 w-32 shrink-0 rounded-(--radius-input) object-cover"
        />
      ) : (
        <div className="flex h-24 w-32 shrink-0 items-center justify-center rounded-(--radius-input) bg-canvas text-xs text-faint">
          Şəkil yoxdur
        </div>
      )}

      <div className="flex flex-1 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-2">
          {listing.status === 'Active' ? (
            <Link to={listingPath(listing.slug, listing.shortId)} className="font-semibold text-ink hover:underline">
              {listing.title}
            </Link>
          ) : (
            <span className="font-semibold text-ink">{listing.title}</span>
          )}

          <Badge>№ {listing.shortId}</Badge>
        </div>

        <p className="text-[15px] font-semibold text-ink">{formatPrice(listing.price, listing.currency)}</p>

        <p className="text-sm text-muted">
          {listing.regionNameAz} · {listing.categoryNameAz} · Baxış: {listing.viewCount}
        </p>

        {listing.rejectionReason ? (
          <p className="text-sm text-accent">
            <strong className="font-semibold">Səbəb:</strong> {listing.rejectionReason}
          </p>
        ) : null}

        {listing.status === 'Active' && listing.expiresAt ? (
          <p className="text-sm text-muted">Bitmə tarixi: {formatDate(listing.expiresAt)}</p>
        ) : null}

        {listing.promotion ? (
          <p className="text-sm font-medium text-interactive" data-testid={testIds.promotionStatus}>
            İrəli çəkilib
            {listing.promotion.expiresAt ? ` · ${formatDate(listing.promotion.expiresAt)}-dək` : ''}
            {` · hər ${listing.promotion.bumpIntervalHours} saatdan bir yenilənir`}
          </p>
        ) : null}

        {listing.restorableUntil ? (
          <p className="text-sm text-muted">Bərpa müddəti: {formatDate(listing.restorableUntil)}</p>
        ) : null}
      </div>

      <div className="flex flex-wrap gap-2">
        {listing.can.edit ? (
          <Link to={`/kabinet/elanlarim/${listing.id}/duzelis`}>
            <Button variant="secondary" size="sm">
              Düzəliş et
            </Button>
          </Link>
        ) : null}

        {listing.can.restore ? (
          <Button variant="primary" size="sm" disabled={busy} onClick={onRestore}>
            Bərpa et
          </Button>
        ) : null}

        {listing.can.markSold ? (
          <Button variant="secondary" size="sm" disabled={busy} onClick={onSold}>
            Satıldı
          </Button>
        ) : null}

        {listing.status === 'Active' && !listing.promotion ? (
          // One promotion at a time: while one runs there is nothing to buy, so no button — the
          // status line above says until when.
          <Button variant="accent" size="sm" disabled={busy} onClick={onPromote}>
            İrəli çək
          </Button>
        ) : null}

        {listing.can.delete ? (
          <Button variant="ghost" size="sm" disabled={busy} onClick={onDelete}>
            Elanı sil
          </Button>
        ) : null}
      </div>
    </li>
  )
}
