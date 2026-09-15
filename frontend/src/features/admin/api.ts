import { api } from '@/api/client'
import type { PagedResult } from '@/features/listings/types'

import type {
  AdminAttribute,
  AdminCategoryNode,
  AdminOverview,
  AdminPaymentOrder,
  AdminPaymentOrderDetail,
  AdminPromotionPackage,
  AdminRegion,
  AdminReport,
  AdminStore,
  AdminUser,
  AuditEntry,
  ModerationDetail,
  ModerationQueueItem,
  RegionImportResult,
} from './types'

/**
 * Every call here maps one-to-one onto an endpoint. There is no business logic on this side: the
 * server decides what a role may do, what a decision means and what gets audited.
 */
export const adminKeys = {
  overview: ['admin', 'overview'] as const,
  moderationQueue: (query: Record<string, string>) => ['admin', 'moderation', query] as const,
  moderationDetail: (id: string) => ['admin', 'moderation', 'detail', id] as const,
  reports: (query: Record<string, string>) => ['admin', 'reports', query] as const,
  stores: (query: Record<string, string>) => ['admin', 'stores', query] as const,
  categories: ['admin', 'categories'] as const,
  regions: ['admin', 'regions'] as const,
  categoryAttributes: (id: number) => ['admin', 'categories', 'attributes', id] as const,
  users: (query: Record<string, string>) => ['admin', 'users', query] as const,
  user: (id: string) => ['admin', 'users', 'detail', id] as const,
  audit: (query: Record<string, string>) => ['admin', 'audit', query] as const,
  paymentOrders: (query: Record<string, string>) => ['admin', 'payments', 'orders', query] as const,
  paymentOrder: (id: string) => ['admin', 'payments', 'orders', 'detail', id] as const,
  promotionPackages: ['admin', 'payments', 'packages'] as const,
}

export function getOverview(): Promise<AdminOverview> {
  return api.get<AdminOverview>('/admin/overview')
}

// ---- listing moderation -----------------------------------------------------------------------

export function getModerationQueue(query: Record<string, string>): Promise<PagedResult<ModerationQueueItem>> {
  return api.get<PagedResult<ModerationQueueItem>>('/admin/moderation/queue', { query })
}

export function getModerationDetail(id: string): Promise<ModerationDetail> {
  return api.get<ModerationDetail>(`/admin/moderation/${id}`)
}

export function approveListing(id: string): Promise<void> {
  return api.post<void>(`/admin/moderation/${id}/approve`)
}

export function rejectListing(id: string, reason: string): Promise<void> {
  return api.post<void>(`/admin/moderation/${id}/reject`, { reason })
}

export function blockListing(id: string, reason: string): Promise<void> {
  return api.post<void>(`/admin/moderation/${id}/block`, { reason })
}

export function unblockListing(id: string): Promise<void> {
  return api.post<void>(`/admin/moderation/${id}/unblock`)
}

// ---- reports ----------------------------------------------------------------------------------

export function getReports(query: Record<string, string>): Promise<PagedResult<AdminReport>> {
  return api.get<PagedResult<AdminReport>>('/admin/reports', { query })
}

export function resolveReport(id: string): Promise<void> {
  return api.post<void>(`/admin/reports/${id}/resolve`)
}

export function dismissReport(id: string): Promise<void> {
  return api.post<void>(`/admin/reports/${id}/dismiss`)
}

// ---- store applications -------------------------------------------------------------------------

export function getAdminStores(query: Record<string, string>): Promise<PagedResult<AdminStore>> {
  return api.get<PagedResult<AdminStore>>('/admin/stores', { query })
}

export function approveStore(id: string): Promise<void> {
  return api.post<void>(`/admin/stores/${id}/approve`)
}

export function rejectStore(id: string, reason: string): Promise<void> {
  return api.post<void>(`/admin/stores/${id}/reject`, { reason })
}

export function suspendStore(id: string, reason: string): Promise<void> {
  return api.post<void>(`/admin/stores/${id}/suspend`, { reason })
}

export function reinstateStore(id: string): Promise<void> {
  return api.post<void>(`/admin/stores/${id}/reinstate`)
}

export function setStoreVerified(id: string, verified: boolean): Promise<void> {
  return api.post<void>(`/admin/stores/${id}/${verified ? 'verify' : 'unverify'}`)
}

// ---- taxonomy -----------------------------------------------------------------------------------

/** Includes the deactivated categories the public tree filters out. */
export function getAdminCategories(): Promise<AdminCategoryNode[]> {
  return api.get<AdminCategoryNode[]>('/admin/categories')
}

/**
 * The complete `UpdateCategoryRequest`. Every field is required because the server assigns all of
 * them: a partial body would silently blank the descriptive and SEO fields it left out, which is
 * why the admin category tree carries them back for the form to round-trip.
 */
export interface CategoryEditBody {
  nameAz: string
  nameRu: string | null
  descriptionAz: string | null
  metaTitleAz: string | null
  metaDescriptionAz: string | null
  iconKey: string | null
  imageKey: string | null
  sortOrder: number
  isActive: boolean
  isSelectable: boolean
}

export function updateCategory(id: number, body: CategoryEditBody): Promise<AdminCategoryNode> {
  return api.put<AdminCategoryNode>(`/admin/categories/${id}`, body)
}

/**
 * Uploads a category picture. The server picks the storage key from a hash of the stored bytes and
 * returns the category, so the caller takes the new key from the response rather than guessing it
 * from the filename.
 */
export function uploadCategoryImage(id: number, file: File): Promise<AdminCategoryNode> {
  const body = new FormData()
  body.append('file', file)

  return api.post<AdminCategoryNode>(`/admin/categories/${id}/image`, body)
}

export function removeCategoryImage(id: number): Promise<AdminCategoryNode> {
  return api.delete<AdminCategoryNode>(`/admin/categories/${id}/image`)
}

export interface ReorderItem {
  id: number
  sortOrder: number
}

/** Persists a whole sibling group at once, the way the endpoint expects. */
export function reorderCategories(items: ReorderItem[]): Promise<void> {
  return api.post<void>('/admin/categories/reorder', { items })
}

export interface CreateAttributeBody {
  categoryId: number
  key: string
  labelAz: string
  dataType: string
  unit: string | null
  isRequired: boolean
  isFilterable: boolean
  sortOrder: number
}

/** The attributes defined directly on a category, with the ids an editor needs. */
export function getCategoryAttributes(categoryId: number): Promise<AdminAttribute[]> {
  return api.get<AdminAttribute[]>(`/admin/categories/${categoryId}/attributes`)
}

export function createAttribute(body: CreateAttributeBody): Promise<number> {
  return api.post<number>('/admin/attributes', body)
}

export interface CreateOptionBody {
  value: string
  labelAz: string
  sortOrder: number
}

export function createAttributeOption(attributeId: number, body: CreateOptionBody): Promise<number> {
  return api.post<number>(`/admin/attributes/${attributeId}/options`, body)
}

export function setCategoryRestriction(id: number, restrictionStatus: string): Promise<unknown> {
  return api.put<unknown>(`/admin/categories/${id}/restriction`, { restrictionStatus })
}

export function deleteCategory(id: number): Promise<void> {
  return api.delete<void>(`/admin/categories/${id}`)
}

export function deleteAttribute(id: number): Promise<void> {
  return api.delete<void>(`/admin/attributes/${id}`)
}

export function deleteAttributeOption(attributeId: number, optionId: number): Promise<void> {
  return api.delete<void>(`/admin/attributes/${attributeId}/options/${optionId}`)
}

// ---- regions --------------------------------------------------------------------------------------

export function getAdminRegions(): Promise<AdminRegion[]> {
  return api.get<AdminRegion[]>('/admin/regions')
}

export function importRegions(regions: unknown[]): Promise<RegionImportResult> {
  return api.post<RegionImportResult>('/admin/regions/import', { regions })
}

// ---- users ----------------------------------------------------------------------------------------

export function getUsers(query: Record<string, string>): Promise<PagedResult<AdminUser>> {
  return api.get<PagedResult<AdminUser>>('/users', { query })
}

export function getUser(id: string): Promise<AdminUser> {
  return api.get<AdminUser>(`/users/${id}`)
}

/** Moderator only — the Admin role is never granted or revoked through the API (PD-7.2). */
export function setModerator(id: string, isModerator: boolean): Promise<AdminUser> {
  return isModerator
    ? api.post<AdminUser>(`/users/${id}/moderator`)
    : api.delete<AdminUser>(`/users/${id}/moderator`)
}

// ---- audit ------------------------------------------------------------------------------------------

export function getAudit(query: Record<string, string>): Promise<PagedResult<AuditEntry>> {
  return api.get<PagedResult<AuditEntry>>('/admin/audit', { query })
}

// ---- payments -------------------------------------------------------------------------------------

export function getPaymentOrders(query: Record<string, string>): Promise<PagedResult<AdminPaymentOrder>> {
  return api.get<PagedResult<AdminPaymentOrder>>('/admin/payment-orders', { query })
}

export function getPaymentOrder(id: string): Promise<AdminPaymentOrderDetail> {
  return api.get<AdminPaymentOrderDetail>(`/admin/payment-orders/${id}`)
}

/**
 * Full refund when `amount` is null; the server validates a partial amount against what actually
 * remains, never against the original total. Either kind always reverses the promotion.
 */
export function refundPaymentOrder(id: string, amount: number | null, reason: string): Promise<void> {
  return api.post<void>(`/admin/payment-orders/${id}/refund`, { amount, reason })
}

export function getAdminPromotionPackages(): Promise<AdminPromotionPackage[]> {
  return api.get<AdminPromotionPackage[]>('/admin/promotion-packages')
}

export interface PromotionPackageBody {
  nameAz: string
  descriptionAz: string | null
  durationDays: number
  priceAzn: number
  sortOrder: number
}

export function createPromotionPackage(body: PromotionPackageBody & { code: string }): Promise<AdminPromotionPackage> {
  return api.post<AdminPromotionPackage>('/admin/promotion-packages', body)
}

/** A package is never deleted — retiring it means `isActive: false`, which hides it from the catalog. */
export function updatePromotionPackage(
  id: number,
  body: PromotionPackageBody & { isActive: boolean },
): Promise<AdminPromotionPackage> {
  return api.put<AdminPromotionPackage>(`/admin/promotion-packages/${id}`, body)
}
