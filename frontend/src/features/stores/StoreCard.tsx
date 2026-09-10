import { Link } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { formatNumber } from '@/features/listings/format'
import { testIds } from '@/testIds'

import type { StoreCard as Card } from './types'
import { storePath } from './types'

/**
 * One row in the directory. Nothing here varies per visitor, which is what lets the directory
 * stay publicly cacheable.
 */
export function StoreCard({ store }: { store: Card }) {
  return (
    <li
      data-testid={testIds.storeCard}
      className="overflow-hidden rounded-(--radius-card) border border-line bg-surface transition-colors hover:border-interactive"
    >
      <Link to={storePath(store.slug)} className="flex items-center gap-3 p-3">
        <StoreLogo name={store.name} url={store.logoUrl} className="size-14 shrink-0" />

        <div className="flex min-w-0 flex-col gap-1">
          <div className="flex items-center gap-2">
            <p className="truncate text-[15px] font-semibold text-ink">{store.name}</p>
            {store.isVerified ? <Badge tone="store">Təsdiqlənmiş</Badge> : null}
          </div>

          <p className="text-xs text-muted">
            {formatNumber(store.listingCount)} elan · {formatNumber(store.followerCount)} izləyici
          </p>
        </div>
      </Link>
    </li>
  )
}

interface StoreLogoProps {
  name: string
  url: string | null
  className?: string
}

/** Falls back to the first letter, so a storefront without a logo still reads as one. */
export function StoreLogo({ name, url, className = 'size-16' }: StoreLogoProps) {
  if (url) {
    return (
      <img
        src={url}
        alt=""
        loading="lazy"
        className={`${className} rounded-(--radius-input) border border-line bg-canvas object-cover`}
      />
    )
  }

  return (
    <span
      aria-hidden="true"
      className={`${className} flex items-center justify-center rounded-(--radius-input) border border-line bg-canvas text-xl font-semibold text-faint`}
    >
      {name.slice(0, 1).toUpperCase()}
    </span>
  )
}
