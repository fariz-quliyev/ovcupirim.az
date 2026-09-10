import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { formatDate, formatNumber } from '@/features/listings/format'

import { getStorePhone, storeKeys } from './api'
import { FollowButton } from './FollowButton'
import { StoreLogo } from './StoreCard'
import type { StorePublic } from './types'

/** Banner, identity and the two counters this phase keeps: listings and followers. */
export function StoreHeader({ store }: { store: StorePublic }) {
  return (
    <header className="overflow-hidden rounded-(--radius-card) border border-line bg-surface">
      {store.bannerUrl ? (
        <img src={store.bannerUrl} alt="" className="h-32 w-full bg-canvas object-cover sm:h-48" />
      ) : (
        <div className="h-20 w-full bg-brand sm:h-28" aria-hidden="true" />
      )}

      <div className="flex flex-col gap-4 p-4 sm:p-5">
        <div className="flex flex-wrap items-start gap-4">
          <StoreLogo name={store.name} url={store.logoUrl} className="-mt-10 size-20 sm:-mt-14 sm:size-24" />

          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-semibold text-ink">{store.name}</h1>
              {store.isVerified ? <Badge tone="store">Təsdiqlənmiş</Badge> : null}
            </div>

            <StoreStats store={store} />
          </div>

          <div className="flex flex-wrap gap-2">
            <FollowButton slug={store.slug} isFollowing={store.isFollowing} />
            <StorePhone slug={store.slug} showPhone={store.showPhone} masked={store.phoneMasked} />
          </div>
        </div>

        {store.description ? (
          <p className="whitespace-pre-line text-[15px] leading-relaxed text-ink">{store.description}</p>
        ) : null}
      </div>
    </header>
  )
}

export function StoreStats({ store }: { store: StorePublic }) {
  return (
    <p className="text-sm text-muted">
      {formatNumber(store.listingCount)} elan · {formatNumber(store.followerCount)} izləyici
      {store.address ? ` · ${store.address}` : ''} · {formatDate(store.memberSince)}-dan bəri
    </p>
  )
}

interface StorePhoneProps {
  slug: string
  showPhone: boolean
  masked: string | null
}

/** The number is never in the page source; it is fetched on request and rate limited server-side. */
function StorePhone({ slug, showPhone, masked }: StorePhoneProps) {
  const [shown, setShown] = useState(false)

  const phone = useQuery({
    queryKey: storeKeys.phone(slug),
    queryFn: () => getStorePhone(slug),
    enabled: shown,
  })

  if (!showPhone) {
    return null
  }

  if (shown && phone.data) {
    return (
      <a href={`tel:${phone.data.phone}`} className="self-center text-lg font-semibold text-interactive">
        {phone.data.phone}
      </a>
    )
  }

  return (
    <Button type="button" variant="secondary" onClick={() => setShown(true)} disabled={phone.isFetching}>
      {phone.isFetching ? 'Yüklənir…' : `Nömrəni göstər · ${masked ?? ''}`}
    </Button>
  )
}
