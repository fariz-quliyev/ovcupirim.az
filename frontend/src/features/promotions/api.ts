import { api } from '@/api/client'
import type { PagedResult } from '@/features/listings/types'

import type { CreatePromotionOrderResult, PaymentOrder, PromotionPackage } from './types'

export const promotionKeys = {
  packages: ['promotions', 'packages'] as const,
  order: (id: string) => ['promotions', 'order', id] as const,
  myOrders: (page: number) => ['promotions', 'my-orders', page] as const,
}

/** The active catalog a seller can buy from. */
export function getPromotionPackages(): Promise<PromotionPackage[]> {
  return api.get<PromotionPackage[]>('/promotion-packages')
}

/** Starts a purchase for the caller's own listing. Sends a package id only — the server prices it. */
export function createPromotionOrder(listingId: string, packageId: number): Promise<CreatePromotionOrderResult> {
  return api.post<CreatePromotionOrderResult>(`/me/listings/${listingId}/promotions/orders`, { packageId })
}

/**
 * The seller's own view of one order, after returning from checkout. Never assume the outcome from
 * the URL the gateway redirected to — this is the one source of truth, backed by the server's own
 * verified callback.
 */
export function getMyPaymentOrder(id: string): Promise<PaymentOrder> {
  return api.get<PaymentOrder>(`/me/payment-orders/${id}`)
}

/** Everything the seller ever ordered, newest first — "Ödənişlərim". */
export function getMyPaymentOrders(page: number): Promise<PagedResult<PaymentOrder>> {
  return api.get<PagedResult<PaymentOrder>>('/me/payment-orders', { query: { page } })
}
