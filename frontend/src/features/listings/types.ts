import type { CategoryPathEntry } from '@/features/catalog/types'
import type { ListingStore } from '@/features/stores/types'

/** Mirrors ListingStatus on the API. */
export type ListingStatus =
  | 'Draft'
  | 'PendingModeration'
  | 'Active'
  | 'Rejected'
  | 'Expired'
  | 'Archived'
  | 'Sold'
  | 'Blocked'

export type ListingCondition = 'New' | 'Used'

/** The tabs a seller sees in "Mənim elanlarım", in the order they are shown. */
export type ListingBucket = 'active' | 'pending' | 'rejected' | 'blocked' | 'expired' | 'sold' | 'draft'

/** Server-computed: what the signed-in seller may do next. The rules live in the state machine. */
export interface ListingCapabilities {
  edit: boolean
  publish: boolean
  delete: boolean
  restore: boolean
  markSold: boolean
}

export interface ListingMedia {
  id: string
  url: string
  variants: Record<string, string>
  width: number
  height: number
  sizeBytes: number
  sortOrder: number
  isPrimary: boolean
}

/** Label and value are rendered server-side against the category schema. */
export interface ListingDisplayAttribute {
  key: string
  labelAz: string
  displayValue: string
}

export interface ListingDetail {
  id: string
  shortId: number
  slug: string
  status: ListingStatus
  rejectionReason: string | null
  categoryId: number
  categorySlug: string
  categoryNameAz: string
  categoryPath: CategoryPathEntry[]
  regionId: number
  regionSlug: string
  regionNameAz: string
  title: string
  description: string
  price: number | null
  currency: string
  condition: ListingCondition
  hasDelivery: boolean
  brand: string | null
  sellerType: string
  contactPhone: string
  showPhone: boolean
  attributes: Record<string, unknown>
  displayAttributes: ListingDisplayAttribute[]
  media: ListingMedia[]
  publishedAt: string | null
  expiresAt: string | null
  restorableUntil: string | null
  viewCount: number
  ageConfirmed: boolean
  createdAt: string
  updatedAt: string | null
  can: ListingCapabilities
}

/** The promotion currently running on the seller's own listing; absent when nothing is active. */
export interface ListingPromotion {
  status: string
  activatedAt: string | null
  expiresAt: string | null
  bumpIntervalHours: number
}

export interface ListingSummary {
  id: string
  shortId: number
  slug: string
  status: ListingStatus
  rejectionReason: string | null
  title: string
  price: number | null
  currency: string
  regionNameAz: string
  categoryNameAz: string
  primaryImageUrl: string | null
  mediaCount: number
  publishedAt: string | null
  expiresAt: string | null
  restorableUntil: string | null
  viewCount: number
  createdAt: string
  can: ListingCapabilities
  promotion: ListingPromotion | null
}

export interface ListingPublic {
  shortId: number
  slug: string
  canonicalPath: string
  title: string
  description: string
  price: number | null
  currency: string
  condition: ListingCondition
  hasDelivery: boolean
  brand: string | null
  sellerType: string
  categoryId: number
  categorySlug: string
  categoryNameAz: string
  categoryPath: CategoryPathEntry[]
  regionSlug: string
  regionNameAz: string
  attributes: ListingDisplayAttribute[]
  media: ListingMedia[]
  /**
   * Null when `store` is set: a listing presented as a storefront's does not disclose the person
   * behind the shop. Populated for an individual sale, and for the fallback when a storefront is
   * no longer active — which is exactly when the page needs a name to show.
   */
  sellerName: string | null
  /** Present only while the listing's storefront is active. A suspended store sends nothing. */
  store: ListingStore | null
  showPhone: boolean
  contactPhoneMasked: string | null
  isFavorited: boolean
  publishedAt: string
  viewCount: number
}

export interface ListingLimit {
  categoryId: number
  categorySlug: string
  categoryNameAz: string
  used: number
  limit: number | null
  nextFreeSlotAt: string | null
}

export interface CreateListingBody {
  categorySlug: string
  regionSlug: string
  title: string
  description: string
  price: number | null
  condition: ListingCondition
  brand: string | null
  hasDelivery: boolean
  contactPhone: string
  showPhone: boolean
  attributes: Record<string, unknown> | null
  /**
   * Files the listing under the seller's own storefront. The server derives sellerType from the
   * outcome, so a client can never claim to be a store.
   */
  useStore?: boolean
}

/** The store link is fixed at creation, so an update never carries it. */
export type UpdateListingBody = Omit<CreateListingBody, 'categorySlug' | 'useStore'>

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

/** A listing as it appears in a result grid — deliberately narrow. */
export interface ListingCard {
  shortId: number
  slug: string
  path: string
  title: string
  price: number | null
  currency: string
  regionNameAz: string
  categorySlug: string
  categoryNameAz: string
  imageUrl: string | null
  hasDelivery: boolean
  condition: ListingCondition
  sellerType: string
  requiresAgeConfirmation: boolean
  isFavorited: boolean
  publishedAt: string
}

export type ListingSort = 'newest' | 'price_asc' | 'price_desc' | 'relevance'

export interface ListingSearchResult {
  items: ListingCard[]
  page: number
  pageSize: number
  total: number
  /** False when the count stopped at its cap; the UI shows "10 000+" instead of a number. */
  totalIsExact: boolean
  totalPages: number
  sort: ListingSort
}

export interface FacetItem {
  slug: string
  nameAz: string
  count: number
}

export interface ListingFacets {
  categories: FacetItem[]
  regions: FacetItem[]
}

export type ReportReason =
  | 'Prohibited'
  | 'Fraud'
  | 'WrongCategory'
  | 'Duplicate'
  | 'MisleadingPrice'
  | 'ForeignPhotos'
  | 'Other'
