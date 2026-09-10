import { Link } from 'react-router'

import { Badge } from '@/components/ui/Badge'
import { testIds } from '@/testIds'

import { formatPrice } from './format'
import type { ListingCard as Card } from './types'

interface ListingCardProps {
  listing: Card
  /** Rendered in the corner when a visitor can save listings. */
  onToggleFavorite?: ((listing: Card) => void) | undefined
}

/**
 * One result tile. Price, title, place and date — the shape a classifieds visitor expects — plus
 * the two markers this marketplace needs: a delivery hint and, for a restricted category, a note
 * that opening it asks for an age confirmation first.
 */
export function ListingCard({ listing, onToggleFavorite }: ListingCardProps) {
  return (
    <li
      data-testid={testIds.listingCard}
      className="relative flex flex-col overflow-hidden rounded-(--radius-card) border border-line bg-surface transition-colors hover:border-interactive"
    >
      {onToggleFavorite ? (
        <button
          type="button"
          aria-label={listing.isFavorited ? 'Seçilmişlərdən çıxar' : 'Seçilmişlərə əlavə et'}
          aria-pressed={listing.isFavorited}
          onClick={() => onToggleFavorite(listing)}
          className="absolute right-2 top-2 z-10 rounded-full bg-surface/90 px-2 py-1 text-base leading-none shadow-sm"
        >
          <span aria-hidden="true">{listing.isFavorited ? '♥' : '♡'}</span>
        </button>
      ) : null}

      <Link to={listing.path} className="flex flex-1 flex-col">
        {listing.imageUrl ? (
          <img
            src={listing.imageUrl}
            alt=""
            loading="lazy"
            className="aspect-4/3 w-full bg-canvas object-cover"
          />
        ) : (
          <div className="flex aspect-4/3 w-full items-center justify-center bg-canvas text-xs text-faint">
            Şəkil yoxdur
          </div>
        )}

        <div className="flex flex-1 flex-col gap-1 p-3">
          <p className="text-[15px] font-semibold text-ink">
            {formatPrice(listing.price, listing.currency)}
          </p>

          <p className="line-clamp-2 text-sm text-ink">{listing.title}</p>

          <p className="mt-auto pt-1 text-xs text-muted">{listing.regionNameAz}</p>

          <div className="flex flex-wrap gap-1.5 pt-1">
            {listing.hasDelivery ? <Badge>Çatdırılma</Badge> : null}
            {listing.condition === 'New' ? <Badge>Yeni</Badge> : null}
            {listing.requiresAgeConfirmation ? <Badge>Yaş təsdiqi</Badge> : null}
          </div>
        </div>
      </Link>
    </li>
  )
}
