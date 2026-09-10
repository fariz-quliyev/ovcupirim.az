/**
 * Stable hooks for end-to-end tests.
 *
 * Kept in one module with no imports of its own: the browser tests run in Node, so importing a
 * component to reach a constant drags in `@/api/client` and its `import.meta.env`, which does not
 * exist outside Vite. Everything here is a plain string, usable from both sides.
 *
 * These exist where a semantic selector is genuinely ambiguous — a queue row whose text runs
 * together with its metadata, a card whose accessible name is assembled from five fields, or a
 * region of the page that arrives after the page itself. Prefer a role or a label everywhere else.
 */
export const testIds = {
  /** The admin queue table, its rows, and its loading and empty states. */
  queueTable: 'queue-table',
  queueRow: 'queue-row',
  queueLoading: 'queue-loading',
  queueEmpty: 'queue-empty',

  /** One result tile in the catalogue, search results or a storefront grid. */
  listingCard: 'listing-card',

  /** The two-pane category chooser, which is populated after the form renders. */
  categoryPicker: 'category-picker',

  /** One row in the public store directory. */
  storeCard: 'store-card',

  /** One row on "Mənim elanlarım". */
  myListingRow: 'my-listing-row',

  /** The package-selection modal opened from "İrəli çək" on a listing row. */
  promotionDialog: 'promotion-dialog',
  promotionPackageOption: 'promotion-package-option',

  /** The heading on the checkout return page; its data-status attribute carries the verified outcome. */
  promotionReturnStatus: 'promotion-return-status',

  /** The "İrəli çəkilib · until" line on a seller's listing row while a promotion runs. */
  promotionStatus: 'promotion-status',

  /** One row on "Ödənişlərim". */
  myPaymentRow: 'my-payment-row',
} as const
