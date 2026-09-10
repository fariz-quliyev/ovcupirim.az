import type { ListingBucket, ListingStatus } from './types'

/**
 * Price presentation, which is where the nullable-price decision becomes visible:
 * a number is shown with the currency, `0` reads as free, and `null` means the seller
 * did not fix a price.
 */
export function formatPrice(price: number | null, currency = 'AZN'): string {
  if (price === null) {
    return 'Razılaşma ilə'
  }

  if (price === 0) {
    return 'Pulsuz'
  }

  return currency === 'AZN' ? `${formatNumber(price)} ₼` : `${formatNumber(price)} ${currency}`
}

/**
 * Grouped by spaces with a comma decimal — "1 650 ₼", "19,99 ₼" — which is how prices read on
 * Azerbaijani classifieds. Built by hand rather than through Intl: runtimes disagree about what
 * "az-AZ" means, and Chromium falls back to English grouping, so the same listing would show a
 * different price in the browser than in a test.
 */
export function formatNumber(value: number): string {
  const rounded = Math.round(value * 100) / 100
  const whole = Math.trunc(Math.abs(rounded))
  const cents = Math.round((Math.abs(rounded) - whole) * 100)

  const grouped = String(whole).replace(/\B(?=(\d{3})+(?!\d))/g, '\u00a0')
  const sign = rounded < 0 ? '-' : ''

  return cents === 0 ? `${sign}${grouped}` : `${sign}${grouped},${String(cents).padStart(2, '0')}`
}

/** Seller-facing status wording, matching the buckets in "Mənim elanlarım". */
export const statusLabels: Record<ListingStatus, string> = {
  Draft: 'Qaralama',
  PendingModeration: 'Gözləmədə',
  Active: 'Hazırda saytda',
  Rejected: 'Dərc olunmamış',
  Expired: 'Müddəti başa çatmış',
  Archived: 'Arxivdə',
  Sold: 'Satılıb',
  Blocked: 'Bloklanıb',
}

export const bucketLabels: Record<ListingBucket, string> = {
  active: 'Hazırda saytda',
  pending: 'Gözləmədə',
  rejected: 'Dərc olunmamış',
  blocked: 'Bloklanıb',
  expired: 'Müddəti başa çatmış',
  sold: 'Satılıb',
  draft: 'Qaralama',
}

/**
 * Tab order, mirroring Tap.az: what is live first, then what needs attention. Rejected and Blocked
 * sit together — both are a moderator's decision the seller needs to see the reason for — with
 * Blocked second because a seller cannot resubmit their way out of it the way a rejection allows.
 */
export const bucketOrder: ListingBucket[] = [
  'active',
  'pending',
  'rejected',
  'blocked',
  'expired',
  'sold',
  'draft',
]

export function listingPath(slug: string, shortId: number): string {
  return `/elan/${slug}-${shortId}`
}

/**
 * Pulls the numeric id out of `{slug}-{shortId}`. The slug is decoration; the number is the key,
 * so a renamed listing keeps working from an old link.
 */
export function shortIdFromParam(param: string | undefined): number | null {
  if (!param) {
    return null
  }

  const match = /-(\d+)$/.exec(param)
  const value = match?.[1]

  return value === undefined ? null : Number(value)
}

export function formatDate(value: string | null): string {
  if (!value) {
    return '—'
  }

  // Built by hand rather than through Intl: runtimes disagree on what "az-AZ" means, and a date
  // that renders as 02.09.2026 in one browser and 2026-09-02 in another is worse than either.
  const date = new Date(value)
  const day = String(date.getDate()).padStart(2, '0')
  const month = String(date.getMonth() + 1).padStart(2, '0')

  return `${day}.${month}.${date.getFullYear()}`
}
