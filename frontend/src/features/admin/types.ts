import type { ListingDetail } from '@/features/listings/types'

/** How long a queue has been waiting, and how much is in it. */
export interface QueueDepth {
  count: number
  oldestWaitingSince: string | null
}

export interface RecentDecisions {
  listingsApproved: number
  listingsRejected: number
  listingsBlocked: number
  reportsResolved: number
  reportsDismissed: number
  storesApproved: number
  storesRejected: number
}

export interface AdminOverview {
  pendingListings: QueueDepth
  strictPendingListings: QueueDepth
  openReports: QueueDepth
  pendingStores: QueueDepth
  /** Captures the gateway confirmed after the order expired — waiting for an operator to refund. */
  lateCaptures: QueueDepth
  last24Hours: RecentDecisions
  databaseReachable: boolean
  generatedAt: string
}

// ---- payments ---------------------------------------------------------------------------------

/** Mirrors PaymentOrderStatus on the API. */
export type AdminPaymentOrderStatus =
  | 'Created'
  | 'AwaitingPayment'
  | 'Paid'
  | 'Failed'
  | 'Expired'
  | 'Canceled'
  | 'Refunded'
  | 'PartiallyRefunded'
  | 'PaidAfterExpiry'

export interface AdminPaymentOrder {
  id: string
  sellerUserId: string
  sellerName: string
  sellerPhone: string
  listingId: string
  listingShortId: number
  listingTitle: string
  packageNameAz: string
  durationDays: number
  amountAzn: number
  refundedAmountAzn: number
  currency: string
  status: AdminPaymentOrderStatus
  promotionStatus: string | null
  provider: string
  providerOrderReference: string | null
  expiresAt: string
  createdAt: string
}

export interface AdminPromotion {
  id: string
  status: string
  activatedAt: string | null
  expiresAt: string | null
  reversedAt: string | null
  reversedReason: string | null
}

/** One row of the append-only payment ledger. */
export interface PaymentTransaction {
  id: string
  eventType: string
  providerReference: string | null
  providerStatusRaw: string | null
  amountAzn: number | null
  payloadJson: string | null
  createdAt: string
}

export interface AdminPaymentOrderDetail {
  order: AdminPaymentOrder
  promotion: AdminPromotion | null
  transactions: PaymentTransaction[]
}

export interface AdminPromotionPackage {
  id: number
  code: string
  nameAz: string
  descriptionAz: string | null
  type: string
  durationDays: number
  priceAzn: number
  currency: string
  isActive: boolean
  sortOrder: number
}

export interface ModerationQueueItem {
  id: string
  shortId: number
  title: string
  categoryNameAz: string
  restrictionStatus: string
  isStrict: boolean
  sellerName: string
  screeningFlags: string[]
  submittedAt: string
}

export interface ModerationHistoryEntry {
  action: string
  reason: string | null
  moderatorName: string
  createdAt: string
}

/**
 * The moderator's view of a listing. `listing.contactPhone` is unmasked here and only here
 * (PD-7.5) — the public page still masks it behind a rate-limited reveal.
 */
export interface ModerationDetail {
  listing: ListingDetail
  sellerName: string
  history: ModerationHistoryEntry[]
}

export type ReportStatus = 'Open' | 'Reviewing' | 'Resolved' | 'Dismissed'

export interface AdminReport {
  id: string
  listingId: string
  listingShortId: number
  listingTitle: string
  reason: string
  comment: string | null
  status: ReportStatus
  isAnonymous: boolean
  createdAt: string
}

export interface AdminStore {
  id: string
  slug: string
  name: string
  description: string | null
  address: string | null
  phone: string | null
  status: string
  isVerified: boolean
  ownerName: string
  ownerUserId: string
  listingCount: number
  createdAt: string
}

export interface AdminCategoryNode {
  id: number
  parentId: number | null
  slug: string
  nameAz: string
  nameRu: string | null
  /** Carried so an editor can send them back unchanged rather than blanking them. */
  descriptionAz: string | null
  metaTitleAz: string | null
  metaDescriptionAz: string | null
  iconKey: string | null
  imageKey: string | null
  sortOrder: number
  depth: number
  isActive: boolean
  isSelectable: boolean
  isLeaf: boolean
  restrictionStatus: string
  listingCount: number
  attributeCount: number
  children: AdminCategoryNode[]
}

export interface AdminRegion {
  id: number
  parentId: number | null
  slug: string
  nameAz: string
  nameRu: string | null
  type: string
  depth: number
  isActive: boolean
  isSelectable: boolean
  sortOrder: number
}

export interface RegionImportResult {
  inserted: number
  updated: number
  skipped: number
}

export interface AdminUser {
  id: string
  phoneNumber: string
  fullName: string
  email: string | null
  role: 'User' | 'Moderator' | 'Admin'
  isPhoneVerified: boolean
  createdAt: string
}

/** The payload comes back parsed, not as an opaque string. */
export interface AuditEntry {
  id: string
  actorUserId: string | null
  actorName: string | null
  entityType: string
  entityId: string
  action: string
  payload: Record<string, unknown> | null
  createdAt: string
}

export interface AdminAttributeOption {
  id: number
  value: string
  labelAz: string
  sortOrder: number
  isActive: boolean
}

/**
 * An attribute as an administrator edits it. Separate from the public `AttributeSchema`, which
 * carries no database identifiers and is not going to grow one for an admin screen.
 */
export interface AdminAttribute {
  id: number
  categoryId: number
  key: string
  labelAz: string
  labelRu: string | null
  dataType: string
  unit: string | null
  isRequired: boolean
  isFilterable: boolean
  isActive: boolean
  appliesToDescendants: boolean
  sortOrder: number
  options: AdminAttributeOption[]
}
