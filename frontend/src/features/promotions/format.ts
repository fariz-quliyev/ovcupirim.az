import type { PaymentOrderStatus } from './types'

/** Seller-facing wording for an order's state, short enough for a table cell. */
export const paymentStatusLabels: Record<PaymentOrderStatus, string> = {
  Created: 'Yaradılıb',
  AwaitingPayment: 'Ödəniş gözlənilir',
  Paid: 'Ödənilib',
  Failed: 'Uğursuz',
  Expired: 'Vaxtı bitib',
  Canceled: 'Ləğv edilib',
  Refunded: 'Geri qaytarılıb',
  PartiallyRefunded: 'Qismən geri qaytarılıb',
  PaidAfterExpiry: 'Gec ödəniş · geri qaytarılacaq',
}
