import { api } from '@/api/client'
import type { CatalogueQuery } from '@/features/listings/api'
import type { ListingSearchResult, PagedResult } from '@/features/listings/types'

import type { StoreApplicationBody, StoreCard, StoreOwner, StorePublic, StoreUpdateBody } from './types'

/** How the directory may be ordered. Mirrors what the endpoint accepts. */
export type StoreDirectorySort = 'name' | 'newest' | 'listings' | 'followers'

export interface StoreDirectoryQuery {
  category?: string | undefined
  sort?: StoreDirectorySort | undefined
  page?: number | undefined
}

export const storeKeys = {
  directory: (query: StoreDirectoryQuery) => ['stores', 'directory', query] as const,
  public: (slug: string) => ['stores', 'public', slug] as const,
  listings: (slug: string, query: CatalogueQuery) => ['stores', 'listings', slug, query] as const,
  phone: (slug: string) => ['stores', 'phone', slug] as const,
  mine: ['stores', 'mine'] as const,
  followed: (page: number) => ['stores', 'followed', page] as const,
}

export function getStoreDirectory(query: StoreDirectoryQuery = {}): Promise<PagedResult<StoreCard>> {
  return api.get<PagedResult<StoreCard>>('/stores', {
    query: {
      category: query.category ?? null,
      sort: query.sort ?? null,
      page: query.page ?? 1,
    },
  })
}

/** 404 for anything that is not an active storefront — pending and suspended alike. */
export function getStore(slug: string): Promise<StorePublic> {
  return api.get<StorePublic>(`/stores/${slug}`)
}

/** The storefront grid. Same catalogue contract as /elanlar, pinned to one store. */
export function getStoreListings(slug: string, query: CatalogueQuery): Promise<ListingSearchResult> {
  return api.get<ListingSearchResult>(`/stores/${slug}/listings`, { query })
}

/** Fetched only when the visitor asks, the way "Nömrəni göstər" works on a listing. */
export function getStorePhone(slug: string): Promise<{ phone: string }> {
  return api.get<{ phone: string }>(`/stores/${slug}/phone`)
}

export function followStore(slug: string): Promise<void> {
  return api.put<void>(`/stores/${slug}/follow`)
}

export function unfollowStore(slug: string): Promise<void> {
  return api.delete<void>(`/stores/${slug}/follow`)
}

export function getFollowedStores(page = 1): Promise<PagedResult<StoreCard>> {
  return api.get<PagedResult<StoreCard>>('/me/followed-stores', { query: { page } })
}

/** The seller's own storefront, in whatever status it currently holds. 404 when they have none. */
export function getMyStore(): Promise<StoreOwner> {
  return api.get<StoreOwner>('/me/store')
}

/** Applies for a storefront. It opens in PendingVerification; an administrator decides. */
export function applyForStore(body: StoreApplicationBody): Promise<StoreOwner> {
  return api.post<StoreOwner>('/me/store', body)
}

/** The name is editable; the slug it produced is not, so the storefront's URL never moves. */
export function updateMyStore(body: StoreUpdateBody): Promise<StoreOwner> {
  return api.put<StoreOwner>('/me/store', body)
}

export function uploadStoreImage(kind: 'logo' | 'banner', file: File): Promise<StoreOwner> {
  const form = new FormData()
  form.append('file', file)

  return api.post<StoreOwner>(`/me/store/${kind}`, form)
}

export function removeStoreImage(kind: 'logo' | 'banner'): Promise<StoreOwner> {
  return api.delete<StoreOwner>(`/me/store/${kind}`)
}
