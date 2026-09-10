import { api } from '@/api/client'

import type {
  CreateListingBody,
  ListingCard,
  ListingFacets,
  ListingSearchResult,
  ReportReason,
  ListingBucket,
  ListingDetail,
  ListingLimit,
  ListingMedia,
  ListingPublic,
  ListingSummary,
  PagedResult,
  UpdateListingBody,
} from './types'

export const listingKeys = {
  mine: (bucket: ListingBucket | 'all') => ['listings', 'mine', bucket] as const,
  detail: (id: string) => ['listings', 'detail', id] as const,
  public: (shortId: number) => ['listings', 'public', shortId] as const,
  limits: ['listings', 'limits'] as const,
}

export function createListing(body: CreateListingBody): Promise<ListingDetail> {
  return api.post<ListingDetail>('/listings', body)
}

export function getListing(id: string): Promise<ListingDetail> {
  return api.get<ListingDetail>(`/listings/${id}`)
}

export function updateListing(id: string, body: UpdateListingBody): Promise<ListingDetail> {
  return api.put<ListingDetail>(`/listings/${id}`, body)
}

/** Submits for moderation. Nothing on this site goes live without a moderator. */
export function publishListing(id: string, ageConfirmed: boolean): Promise<ListingDetail> {
  return api.post<ListingDetail>(`/listings/${id}/publish`, { ageConfirmed })
}

export function restoreListing(id: string, ageConfirmed = false): Promise<ListingDetail> {
  return api.post<ListingDetail>(`/listings/${id}/restore`, { ageConfirmed })
}

export function markListingSold(id: string): Promise<ListingDetail> {
  return api.post<ListingDetail>(`/listings/${id}/mark-sold`)
}

/**
 * "Elanı sil". A draft is removed; a submitted listing retires and stays restorable for 30 days.
 * A POST rather than a DELETE, because for a published listing this is a reversible state change.
 */
export function deleteListing(id: string): Promise<void> {
  return api.post<void>(`/listings/${id}/delete`)
}

export function getMyListings(
  bucket: ListingBucket | 'all',
  page = 1,
): Promise<PagedResult<ListingSummary>> {
  return api.get<PagedResult<ListingSummary>>('/me/listings', {
    query: { status: bucket === 'all' ? undefined : bucket, page },
  })
}

export function getMyListingLimits(): Promise<ListingLimit[]> {
  return api.get<ListingLimit[]>('/me/listing-limits')
}

export function uploadListingMedia(id: string, file: File): Promise<ListingMedia> {
  const form = new FormData()
  form.append('file', file)

  return api.post<ListingMedia>(`/listings/${id}/media`, form)
}

export function deleteListingMedia(id: string, mediaId: string): Promise<void> {
  return api.delete<void>(`/listings/${id}/media/${mediaId}`)
}

export function reorderListingMedia(id: string, mediaIds: string[]): Promise<ListingMedia[]> {
  return api.put<ListingMedia[]>(`/listings/${id}/media/order`, { mediaIds })
}

/**
 * Restricted and unclassified categories stay browsable, but the server withholds the page until
 * the visitor acknowledges the age requirement.
 */
export function getPublicListing(shortId: number, ageConfirmed = false): Promise<ListingPublic> {
  return api.get<ListingPublic>(`/listings/by-short-id/${shortId}`, {
    query: { ageConfirmed: ageConfirmed ? 'true' : null },
  })
}

/** Fetched only when the buyer asks, the way "Nömrəni göstər" works. */
export function getPublicListingPhone(shortId: number): Promise<{ contactPhone: string }> {
  return api.get<{ contactPhone: string }>(`/listings/by-short-id/${shortId}/phone`)
}

/** The catalogue query, exactly as it travels in the URL. */
export type CatalogueQuery = Record<string, string>

export const catalogueKeys = {
  search: (query: CatalogueQuery) => ['listings', 'search', query] as const,
  facets: (query: CatalogueQuery) => ['listings', 'facets', query] as const,
  similar: (shortId: number) => ['listings', 'similar', shortId] as const,
  favorites: (page: number) => ['listings', 'favorites', page] as const,
}

export function searchListings(query: CatalogueQuery): Promise<ListingSearchResult> {
  return api.get<ListingSearchResult>('/listings', { query })
}

export function getListingFacets(query: CatalogueQuery): Promise<ListingFacets> {
  return api.get<ListingFacets>('/listings/facets', { query })
}

export function getSimilarListings(shortId: number): Promise<ListingCard[]> {
  return api.get<ListingCard[]>(`/listings/by-short-id/${shortId}/similar`)
}

export function getFavorites(page = 1): Promise<PagedResult<ListingCard>> {
  return api.get<PagedResult<ListingCard>>('/me/favorites', { query: { page } })
}

/** Addressed by the public listing number, the same identifier the card and the URL carry. */
export function addFavorite(shortId: number): Promise<void> {
  return api.put<void>(`/me/favorites/${shortId}`)
}

export function removeFavorite(shortId: number): Promise<void> {
  return api.delete<void>(`/me/favorites/${shortId}`)
}

export function reportListing(
  shortId: number,
  reason: ReportReason,
  comment: string | null,
): Promise<void> {
  return api.post<void>(`/listings/by-short-id/${shortId}/report`, { reason, comment })
}
