/** A purchasable promotion offering — the public catalog a seller chooses from. */
export interface PromotionPackage {
  id: number
  code: string
  nameAz: string
  descriptionAz: string | null
  durationDays: number
  priceAzn: number
  currency: string
  /** How often the listing is lifted back to the top while the promotion runs. */
  bumpIntervalHours: number
}

/** Mirrors PaymentOrderStatus on the API. */
export type PaymentOrderStatus =
  | 'Created'
  | 'AwaitingPayment'
  | 'Paid'
  | 'Failed'
  | 'Expired'
  | 'Canceled'
  | 'Refunded'
  | 'PartiallyRefunded'
  | 'PaidAfterExpiry'

export interface PaymentOrder {
  id: string
  listingId: string
  promotionPackageId: number
  packageNameAz: string
  /** Frozen at purchase, next to the price — what this order bought, whatever the package says now. */
  durationDays: number
  amountAzn: number
  currency: string
  status: PaymentOrderStatus
  expiresAt: string
  createdAt: string
}

export interface CreatePromotionOrderResult {
  paymentOrderId: string
  redirectUrl: string
}
