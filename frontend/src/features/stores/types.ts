/** Mirrors StoreStatus on the API. There is deliberately no `Rejected`: a refused
 *  application is withdrawn rather than kept as a tombstone, so the seller may apply again. */
export type StoreStatus = 'PendingVerification' | 'Active' | 'Suspended'

/** The owner's own view. Visible in every status, including one still awaiting approval. */
export interface StoreOwner {
  id: string
  slug: string
  name: string
  description: string | null
  address: string | null
  phone: string | null
  status: StoreStatus
  isVerified: boolean
  /** True only while the storefront is actually reachable at /magaza/{slug}. */
  isPublic: boolean
  logoUrl: string | null
  bannerUrl: string | null
  listingCount: number
  followerCount: number
  createdAt: string
}

/** The public storefront. Carries this visitor's own `isFollowing`. */
export interface StorePublic {
  slug: string
  name: string
  description: string | null
  address: string | null
  isVerified: boolean
  logoUrl: string | null
  bannerUrl: string | null
  listingCount: number
  followerCount: number
  showPhone: boolean
  phoneMasked: string | null
  isFollowing: boolean
  memberSince: string
}

/** A row in the directory, and in "İzlədiyim mağazalar". */
export interface StoreCard {
  slug: string
  name: string
  logoUrl: string | null
  isVerified: boolean
  listingCount: number
  followerCount: number
  memberSince: string
}

/** The block a listing carries when it belongs to a storefront that is currently active. */
export interface ListingStore {
  slug: string
  name: string
  isVerified: boolean
  logoUrl: string | null
}

export interface StoreApplicationBody {
  name: string
  description: string | null
  address: string | null
  phone: string | null
}

export type StoreUpdateBody = StoreApplicationBody

export function storePath(slug: string): string {
  return `/magaza/${slug}`
}
