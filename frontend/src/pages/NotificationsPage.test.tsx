import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import {
  jsonResponse,
  mockFetchByUrl,
  problemResponse,
  renderWithProviders,
  testAuthResponse,
} from '@/features/auth/authTestUtils'
import type { Notification } from '@/features/notifications/types'

import { NotificationsPage } from './NotificationsPage'

function notification(overrides: Partial<Notification> = {}): Notification {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    type: 'listing.rejected',
    title: 'Elan rədd edildi',
    body: '"Ov bel çantası" elanı rədd edildi. Səbəb: Şəkillər kifayət deyil.',
    entityType: 'Listing',
    entityId: '22222222-2222-2222-2222-222222222222',
    isRead: false,
    createdAt: '2026-09-03T10:00:00+00:00',
    ...overrides,
  }
}

function page(items: Notification[]) {
  return { items, page: 1, pageSize: 24, total: items.length, totalPages: 1 }
}

function mockNotifications(items: Notification[]) {
  setAccessToken(testAuthResponse.accessToken)

  const fetchMock = mockFetchByUrl({
    '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    '/me/notifications/unread-count': () =>
      jsonResponse({ count: items.filter((n) => !n.isRead).length }),
    '/me/notifications/read-all': () => new Response(null, { status: 204 }),
    '/me/notifications': () => jsonResponse(page(items)),
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('NotificationsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('says so when there is nothing to show', async () => {
    mockNotifications([])
    renderWithProviders(<NotificationsPage />)

    expect(await screen.findByText('Hələ bildiriş yoxdur.')).toBeInTheDocument()
  })

  it('lists notifications newest first with their reason in the body', async () => {
    mockNotifications([notification()])
    renderWithProviders(<NotificationsPage />)

    expect(await screen.findByText('Elan rədd edildi')).toBeInTheDocument()
    expect(screen.getByText(/Şəkillər kifayət deyil\./)).toBeInTheDocument()
  })

  it('marks a notification read when it is opened, and the badge disappears', async () => {
    const fetchMock = mockNotifications([notification()])
    renderWithProviders(<NotificationsPage />)

    const card = await screen.findByRole('button', { name: /Elan rədd edildi/ })

    // The unread dot is present before the click.
    expect(card.querySelector('[aria-hidden="true"]')).not.toBeNull()

    await userEvent.click(card)

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith(`/me/notifications/${notification().id}/read`) &&
          (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
    })
  })

  it('does not re-send a read request for an already-read notification', async () => {
    const fetchMock = mockNotifications([notification({ isRead: true })])
    renderWithProviders(<NotificationsPage />)

    const card = await screen.findByRole('button', { name: /Elan rədd edildi/ })
    await userEvent.click(card)

    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes('/read')),
    ).toBe(false)
  })

  it('offers "mark all read" only while something is unread', async () => {
    mockNotifications([notification({ isRead: true })])
    renderWithProviders(<NotificationsPage />)

    await screen.findByText('Elan rədd edildi')

    expect(screen.queryByRole('button', { name: 'Hamısını oxunmuş et' })).not.toBeInTheDocument()
  })

  it('marks every notification read at once', async () => {
    const fetchMock = mockNotifications([notification(), notification({ id: 'second', title: 'İkinci' })])
    renderWithProviders(<NotificationsPage />)

    const button = await screen.findByRole('button', { name: 'Hamısını oxunmuş et' })
    await userEvent.click(button)

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith('/me/notifications/read-all') &&
          (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
    })
  })
})
