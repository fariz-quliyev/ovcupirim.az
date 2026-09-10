import { describe, expect, it } from 'vitest'

import { formatDate, formatPrice, listingPath, shortIdFromParam } from './format'

/**
 * Price presentation is where the nullable-price decision shows up on screen, so all three cases
 * are pinned here rather than left to a component test.
 */
describe('formatPrice', () => {
  it('shows a normal price with the manat sign', () => {
    expect(formatPrice(150)).toBe('150 ₼')
  })

  it('keeps decimals only when the price has them', () => {
    expect(formatPrice(19.99)).toBe('19,99 ₼')
  })

  it('groups thousands with a space, the way a price reads here', () => {
    // A non-breaking space, so a price never wraps mid-number.
    expect(formatPrice(1200)).toBe('1\u00a0200 ₼')
    expect(formatPrice(1368.57)).toBe('1\u00a0368,57 ₼')
  })

  it('formats the same way in every runtime rather than relying on locale data', () => {
    // Chromium and Node disagree about az-AZ; the output must not.
    expect(formatPrice(1234.5)).toBe('1\u00a0234,50 ₼')
  })

  it('reads zero as free rather than as "0 ₼"', () => {
    expect(formatPrice(0)).toBe('Pulsuz')
  })

  it('reads a missing price as negotiable', () => {
    expect(formatPrice(null)).toBe('Razılaşma ilə')
  })

  it('falls back to the currency code for anything but manat', () => {
    expect(formatPrice(50, 'USD')).toBe('50 USD')
  })
})

describe('listing URLs', () => {
  it('builds the canonical path from the slug and the number', () => {
    expect(listingPath('ov-bel-cantasi', 2001)).toBe('/elan/ov-bel-cantasi-2001')
  })

  it('reads the number back out, which is the part that identifies the listing', () => {
    expect(shortIdFromParam('ov-bel-cantasi-2001')).toBe(2001)
  })

  it('still resolves when the slug itself contains digits and hyphens', () => {
    expect(shortIdFromParam('cadir-2-neferlik-30l-48580650')).toBe(48580650)
  })

  it('returns null when there is no number to read', () => {
    expect(shortIdFromParam('ov-bel-cantasi')).toBeNull()
    expect(shortIdFromParam(undefined)).toBeNull()
  })
})

describe('formatDate', () => {
  it('renders a numeric date rather than relying on Azerbaijani month names', () => {
    expect(formatDate('2026-09-02T10:00:00+00:00')).toMatch(/^\d{2}\.\d{2}\.\d{4}$/)
  })

  it('shows a dash when there is no date', () => {
    expect(formatDate(null)).toBe('—')
  })
})
