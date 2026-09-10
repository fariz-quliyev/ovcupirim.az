import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { AuthResponse, User, UserRole } from '@/features/auth/types'
import { AdminLayout } from '@/layouts/AdminLayout'

import { AdminDashboardPage } from './AdminDashboardPage'
import { AdminPackagesPage } from './AdminPackagesPage'
import { AdminPaymentsPage } from './AdminPaymentsPage'
import { AdminTaxonomyPage } from './AdminTaxonomyPage'
import { AdminUsersPage } from './AdminUsersPage'
import { AdminModerationPage } from './AdminModerationPage'
import { AdminStoresPage } from './AdminStoresPage'

const overview = {
  pendingListings: { count: 3, oldestWaitingSince: '2026-09-03T08:00:00+00:00' },
  strictPendingListings: { count: 1, oldestWaitingSince: '2026-09-03T08:00:00+00:00' },
  openReports: { count: 2, oldestWaitingSince: '2026-09-03T09:00:00+00:00' },
  pendingStores: { count: 0, oldestWaitingSince: null },
  lateCaptures: { count: 1, oldestWaitingSince: '2026-09-03T11:00:00+00:00' },
  last24Hours: {
    listingsApproved: 12,
    listingsRejected: 3,
    listingsBlocked: 1,
    reportsResolved: 4,
    reportsDismissed: 2,
    storesApproved: 1,
    storesRejected: 0,
  },
  databaseReachable: true,
  generatedAt: '2026-09-03T12:00:00+00:00',
}

const queueItem = {
  id: '11111111-1111-1111-1111-111111111111',
  shortId: 4001,
  title: 'Yoxlama çantası',
  categoryNameAz: 'Bel çantası',
  restrictionStatus: 'Unrestricted',
  isStrict: false,
  sellerName: 'Test Satıcı',
  screeningFlags: [],
  submittedAt: '2026-09-03T10:00:00+00:00',
}

const moderationDetail = {
  listing: {
    id: queueItem.id,
    shortId: 4001,
    slug: 'yoxlama-cantasi',
    status: 'PendingModeration',
    rejectionReason: null,
    categoryId: 37,
    categorySlug: 'bel-cantasi',
    categoryNameAz: 'Bel çantası',
    categoryPath: [],
    regionId: 13,
    regionSlug: 'baki',
    regionNameAz: 'Bakı',
    title: 'Yoxlama çantası',
    description: 'Yoxlama təsviri.',
    price: 120,
    currency: 'AZN',
    condition: 'Used',
    hasDelivery: false,
    brand: null,
    sellerType: 'Individual',
    contactPhone: '+994501234567',
    showPhone: true,
    attributes: {},
    displayAttributes: [],
    media: [],
    publishedAt: null,
    expiresAt: null,
    restorableUntil: null,
    viewCount: 0,
    ageConfirmed: false,
    createdAt: '2026-09-03T10:00:00+00:00',
    updatedAt: null,
    can: { edit: false, publish: false, delete: false, restore: false, markSold: false },
  },
  sellerName: 'Test Satıcı',
  history: [],
}

const storeRow = {
  id: '22222222-2222-2222-2222-222222222222',
  slug: 'ovcu-dunyasi',
  name: 'Ovçu Dünyası',
  description: null,
  address: null,
  phone: '+994501112233',
  status: 'Active',
  isVerified: false,
  ownerName: 'Test Satıcı',
  ownerUserId: '33333333-3333-3333-3333-333333333333',
  listingCount: 4,
  createdAt: '2026-09-01T10:00:00+00:00',
}

function paged<T>(items: T[]) {
  return { items, page: 1, pageSize: 24, total: items.length, totalPages: 1 }
}

function operator(role: UserRole): User {
  return {
    id: '99999999-9999-9999-9999-999999999999',
    phoneNumber: '+994701234567',
    fullName: 'Operator',
    email: null,
    role,
    isPhoneVerified: true,
    createdAt: '2026-01-01T00:00:00+00:00',
  }
}

/**
 * Routes by URL *and* method, which the admin screens need: the same path serves a queue on GET and
 * a decision on POST.
 */
function adminFetch(role: UserRole, routes: Record<string, (init?: RequestInit) => Response>) {
  const user = operator(role)
  const auth: AuthResponse = { accessToken: 'test-token', expiresInSeconds: 900, user }

  setAccessToken(auth.accessToken)

  return vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)

    if (url.includes('/auth/refresh')) return Promise.resolve(jsonResponse(auth))
    if (url.includes('/auth/me') || url.endsWith('/users/me')) return Promise.resolve(jsonResponse(user))

    const match = Object.keys(routes).find((fragment) => url.includes(fragment))

    return Promise.resolve(
      match ? routes[match]!(init) : problemResponse(404, `No stub for ${url}`),
    )
  })
}

describe('AdminLayout navigation', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  function renderLayout(role: UserRole) {
    vi.stubGlobal(
      'fetch',
      adminFetch(role, { '/admin/overview': () => jsonResponse(overview) }),
    )

    return renderWithProviders(
      <Routes>
        <Route element={<AdminLayout />}>
          <Route path="/admin" element={<div>İcmal məzmunu</div>} />
        </Route>
      </Routes>,
      { route: '/admin' },
    )
  }

  it('shows an admin every section', async () => {
    renderLayout('Admin')

    const nav = await screen.findByRole('navigation', { name: 'İdarəetmə bölmələri' })

    for (const label of [
      'İcmal',
      'Elan moderasiyası',
      'Şikayətlər',
      'Mağaza müraciətləri',
      'Ödənişlər',
      'Paketlər',
      'Kateqoriyalar',
      'Regionlar',
      'İstifadəçilər',
      'Audit jurnalı',
    ]) {
      expect(within(nav).getByRole('link', { name: new RegExp(label) })).toBeInTheDocument()
    }
  })

  it('never shows a moderator the admin-only sections', async () => {
    renderLayout('Moderator')

    const nav = await screen.findByRole('navigation', { name: 'İdarəetmə bölmələri' })

    expect(within(nav).getByRole('link', { name: /Elan moderasiyası/ })).toBeInTheDocument()
    expect(within(nav).getByRole('link', { name: /Şikayətlər/ })).toBeInTheDocument()

    for (const hidden of [
      'Mağaza müraciətləri',
      'Ödənişlər',
      'Paketlər',
      'Kateqoriyalar',
      'Regionlar',
      'İstifadəçilər',
      'Audit jurnalı',
    ]) {
      expect(within(nav).queryByRole('link', { name: new RegExp(hidden) })).not.toBeInTheDocument()
    }
  })

  it('badges a section with the work waiting in it', async () => {
    renderLayout('Admin')

    const nav = await screen.findByRole('navigation', { name: 'İdarəetmə bölmələri' })

    // The badge appears once the overview lands.
    await waitFor(() =>
      expect(within(nav).getByRole('link', { name: /Elan moderasiyası/ })).toHaveTextContent('3'),
    )

    // A queue with nothing in it carries no badge at all.
    expect(within(nav).getByRole('link', { name: /Mağaza müraciətləri/ })).not.toHaveTextContent('0')

    // A late capture waiting for a refund is work too.
    expect(within(nav).getByRole('link', { name: /Ödənişlər/ })).toHaveTextContent('1')
  })
})

const paymentOrder = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  sellerUserId: '22222222-2222-2222-2222-222222222222',
  sellerName: 'Test Satıcı',
  sellerPhone: '+994501234567',
  listingId: '11111111-1111-1111-1111-111111111111',
  listingShortId: 4001,
  listingTitle: 'Yoxlama çantası',
  packageNameAz: 'İrəli çək — 7 gün',
  durationDays: 7,
  amountAzn: 2,
  refundedAmountAzn: 0,
  currency: 'AZN',
  status: 'Paid',
  promotionStatus: 'Active',
  provider: 'Epoint',
  providerOrderReference: 'epoint-123',
  expiresAt: '2026-09-03T10:30:00+00:00',
  createdAt: '2026-09-03T10:00:00+00:00',
}

const paymentDetail = {
  order: paymentOrder,
  promotion: {
    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    status: 'Active',
    activatedAt: '2026-09-03T10:05:00+00:00',
    expiresAt: '2026-09-10T10:05:00+00:00',
    reversedAt: null,
    reversedReason: null,
  },
  transactions: [
    {
      id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
      eventType: 'OrderCreated',
      providerReference: 'epoint-123',
      providerStatusRaw: null,
      amountAzn: 2,
      payloadJson: null,
      createdAt: '2026-09-03T10:00:00+00:00',
    },
    {
      id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
      eventType: 'StatusChecked',
      providerReference: 'epoint-123',
      providerStatusRaw: 'success',
      amountAzn: 2,
      payloadJson: null,
      createdAt: '2026-09-03T10:05:00+00:00',
    },
  ],
}

describe('AdminPaymentsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('lists orders and opens the ledger behind one', async () => {
    vi.stubGlobal(
      'fetch',
      adminFetch('Admin', {
        // More specific first: the list fragment is a prefix of the detail URL.
        [`/admin/payment-orders/${paymentOrder.id}`]: () => jsonResponse(paymentDetail),
        '/admin/payment-orders': () => jsonResponse(paged([paymentOrder])),
      }),
    )

    renderWithProviders(<AdminPaymentsPage />, { route: '/admin/payments' })

    const row = (await screen.findAllByTestId('queue-row'))[0]!
    expect(row).toHaveTextContent('№ 4001')
    expect(row).toHaveTextContent('Ödənilib')
    expect(row).toHaveTextContent('Aktiv promosiya')

    await userEvent.click(within(row).getByRole('button', { name: 'Aç' }))

    const pane = await screen.findByRole('region', { name: 'Sifariş detalları' })
    expect(await within(pane).findByText('Status yoxlanıldı')).toBeInTheDocument()
    expect(within(pane).getByText('epoint-123', { selector: 'dd' })).toBeInTheDocument()
    expect(within(pane).getByRole('button', { name: 'Geri qaytar' })).toBeInTheDocument()
  })

  it('refunds with a reason, sending an amount only when one is typed', async () => {
    const fetchMock = adminFetch('Admin', {
      [`/admin/payment-orders/${paymentOrder.id}/refund`]: () => new Response(null, { status: 204 }),
      [`/admin/payment-orders/${paymentOrder.id}`]: () => jsonResponse(paymentDetail),
      '/admin/payment-orders': () => jsonResponse(paged([paymentOrder])),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AdminPaymentsPage />, { route: '/admin/payments' })

    const row = (await screen.findAllByTestId('queue-row'))[0]!
    await userEvent.click(within(row).getByRole('button', { name: 'Aç' }))

    const pane = await screen.findByRole('region', { name: 'Sifariş detalları' })
    await userEvent.click(await within(pane).findByRole('button', { name: 'Geri qaytar' }))

    const dialog = screen.getByRole('dialog', { name: 'Ödənişi geri qaytar' })
    // Nothing goes out without a reason — the button stays disabled until one is typed.
    expect(within(dialog).getByRole('button', { name: 'Geri qaytar' })).toBeDisabled()

    await userEvent.type(within(dialog).getByLabelText('Səbəb *'), 'Alıcı şikayəti')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Geri qaytar' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Ödənişi geri qaytar' })).not.toBeInTheDocument())

    const refundCall = fetchMock.mock.calls.find(([url]) => String(url).includes('/refund'))
    expect(refundCall).toBeDefined()
    const body = JSON.parse((refundCall![1] as RequestInit).body as string) as Record<string, unknown>
    expect(body).toEqual({ amount: null, reason: 'Alıcı şikayəti' })
  })
})

describe('AdminPackagesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('creates a package with every field the server assigns', async () => {
    const created = {
      id: 7,
      code: 'bump-3d',
      nameAz: 'İrəli çək — 3 gün',
      descriptionAz: null,
      type: 'Bump',
      durationDays: 3,
      priceAzn: 1,
      currency: 'AZN',
      isActive: true,
      sortOrder: 10,
    }

    const fetchMock = adminFetch('Admin', {
      '/admin/promotion-packages': (init) =>
        init?.method === 'POST' ? jsonResponse(created) : jsonResponse([]),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AdminPackagesPage />, { route: '/admin/packages' })

    expect(await screen.findByText('Hələ paket yoxdur.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Yeni paket' }))
    const dialog = screen.getByRole('dialog', { name: 'Yeni paket' })

    await userEvent.type(within(dialog).getByLabelText('Kod *'), 'bump-3d')
    await userEvent.type(within(dialog).getByLabelText('Ad *'), 'İrəli çək — 3 gün')
    await userEvent.clear(within(dialog).getByLabelText('Müddət (gün) *'))
    await userEvent.type(within(dialog).getByLabelText('Müddət (gün) *'), '3')
    await userEvent.type(within(dialog).getByLabelText('Qiymət (AZN) *'), '1')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Yarat' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Yeni paket' })).not.toBeInTheDocument())

    const createCall = fetchMock.mock.calls.find(
      ([url, init]) => String(url).includes('/admin/promotion-packages') && (init as RequestInit)?.method === 'POST',
    )
    expect(createCall).toBeDefined()
    const body = JSON.parse((createCall![1] as RequestInit).body as string) as Record<string, unknown>
    expect(body).toEqual({
      code: 'bump-3d',
      nameAz: 'İrəli çək — 3 gün',
      descriptionAz: null,
      durationDays: 3,
      priceAzn: 1,
      sortOrder: 10,
    })
  })

  it('edits a package without ever offering to delete it', async () => {
    const existing = {
      id: 7,
      code: 'bump-3d',
      nameAz: 'İrəli çək — 3 gün',
      descriptionAz: null,
      type: 'Bump',
      durationDays: 3,
      priceAzn: 1,
      currency: 'AZN',
      isActive: true,
      sortOrder: 10,
    }

    const fetchMock = adminFetch('Admin', {
      '/admin/promotion-packages/7': () => jsonResponse({ ...existing, isActive: false }),
      '/admin/promotion-packages': () => jsonResponse([existing]),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AdminPackagesPage />, { route: '/admin/packages' })

    const row = (await screen.findAllByTestId('queue-row'))[0]!
    expect(screen.queryByRole('button', { name: /Sil/ })).not.toBeInTheDocument()

    await userEvent.click(within(row).getByRole('button', { name: 'Redaktə et' }))
    const dialog = screen.getByRole('dialog', { name: 'Paketi redaktə et' })

    await userEvent.click(within(dialog).getByLabelText('Kataloqda aktivdir'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Yadda saxla' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Paketi redaktə et' })).not.toBeInTheDocument())

    const updateCall = fetchMock.mock.calls.find(
      ([url, init]) => String(url).includes('/admin/promotion-packages/7') && (init as RequestInit)?.method === 'PUT',
    )
    expect(updateCall).toBeDefined()
    const body = JSON.parse((updateCall![1] as RequestInit).body as string) as Record<string, unknown>
    expect(body).toEqual({
      nameAz: 'İrəli çək — 3 gün',
      descriptionAz: null,
      durationDays: 3,
      priceAzn: 1,
      sortOrder: 10,
      isActive: false,
    })
  })
})

describe('AdminDashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('shows queue depths, waiting times and the last day of decisions', async () => {
    vi.stubGlobal('fetch', adminFetch('Admin', { '/admin/overview': () => jsonResponse(overview) }))

    renderWithProviders(<AdminDashboardPage />, { route: '/admin' })

    expect(await screen.findByText('Gözləyən elanlar')).toBeInTheDocument()
    expect(screen.getByText('Növbə boşdur')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('Əlçatandır')).toBeInTheDocument()
  })
})

describe('AdminModerationPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  function renderModeration(routes: Record<string, (init?: RequestInit) => Response>) {
    const fetchMock = adminFetch('Moderator', routes)
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(<AdminModerationPage />, { route: '/admin/moderation' })

    return fetchMock
  }

  it('lists what is waiting and opens a decision pane', async () => {
    renderModeration({
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}`]: () => jsonResponse(moderationDetail),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))

    // The pane is the point of the screen: a row cannot tell a moderator whether to approve.
    expect(await screen.findByText('Yoxlama təsviri.')).toBeInTheDocument()
    expect(screen.getByText('Bu elana dair hələ qərar verilməyib.')).toBeInTheDocument()
  })

  it('shows the contact number unmasked to a moderator', async () => {
    renderModeration({
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}`]: () => jsonResponse(moderationDetail),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))

    // PD-7.5. The public page still masks it behind a rate-limited reveal.
    expect(await screen.findByText('+994501234567')).toBeInTheDocument()
  })

  it('requires a reason before it will reject', async () => {
    const fetchMock = renderModeration({
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}`]: () => jsonResponse(moderationDetail),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Rədd et' }))

    const dialog = await screen.findByRole('dialog', { name: 'Elanı rədd et' })
    const confirm = within(dialog).getByRole('button', { name: 'Rədd et' })

    expect(confirm).toBeDisabled()

    await userEvent.type(within(dialog).getByLabelText('Səbəb *'), 'Şəkillər kifayət deyil.')
    expect(confirm).toBeEnabled()

    await userEvent.click(confirm)

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith(`/admin/moderation/${queueItem.id}/reject`) &&
          (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({
        reason: 'Şəkillər kifayət deyil.',
      })
    })
  })

  it('presents a lost race as a workflow state rather than a failure', async () => {
    renderModeration({
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}/approve`]: () =>
        problemResponse(409, 'Bu elan artıq moderasiyadan keçib.'),
      [`/admin/moderation/${queueItem.id}`]: () => jsonResponse(moderationDetail),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Təsdiqlə' }))

    // Told plainly that someone got there first, and shown the refreshed queue — not a red toast.
    const notice = await screen.findByRole('status')

    expect(notice).toHaveTextContent('Bu elan artıq moderasiyadan keçib.')
    expect(notice).toHaveTextContent('Növbə yeniləndi.')
  })

  it('switches to the active queue and asks the server for it', async () => {
    const fetchMock = renderModeration({
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
    })

    await userEvent.selectOptions(await screen.findByLabelText('Növbə'), 'Aktiv')

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) => String(url).includes('/admin/moderation/queue?status=active')),
      ).toBe(true),
    )
  })

  it('offers only blocking on an active listing, never approve or reject', async () => {
    renderModeration({
      '/admin/moderation/queue?status=active': () => jsonResponse(paged([queueItem])),
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}`]: () =>
        jsonResponse({
          ...moderationDetail,
          listing: { ...moderationDetail.listing, status: 'Active' },
        }),
    })

    await userEvent.selectOptions(await screen.findByLabelText('Növbə'), 'Aktiv')
    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))

    expect(await screen.findByRole('button', { name: 'Blokla' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Təsdiqlə' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Rədd et' })).not.toBeInTheDocument()
  })

  it('blocks an active listing with a reason, and it leaves the active queue', async () => {
    const fetchMock = renderModeration({
      '/admin/moderation/queue?status=active': () => jsonResponse(paged([queueItem])),
      '/admin/moderation/queue': () => jsonResponse(paged([queueItem])),
      [`/admin/moderation/${queueItem.id}`]: () =>
        jsonResponse({
          ...moderationDetail,
          listing: { ...moderationDetail.listing, status: 'Active' },
        }),
      [`/admin/moderation/${queueItem.id}/block`]: () => new Response(null, { status: 204 }),
    })

    await userEvent.selectOptions(await screen.findByLabelText('Növbə'), 'Aktiv')
    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Blokla' }))

    const dialog = await screen.findByRole('dialog', { name: 'Elanı blokla' })
    await userEvent.type(within(dialog).getByLabelText('Səbəb *'), 'Qaydalara zidd məzmun.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Blokla' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith(`/admin/moderation/${queueItem.id}/block`) &&
          (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({
        reason: 'Qaydalara zidd məzmun.',
      })
    })

    // Same signal as everywhere else in the panel: the decision landed once the pane closes and the
    // queue is asked for again, not when the click happened.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('AdminStoresPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('says what suspension actually does before it does it', async () => {
    vi.stubGlobal(
      'fetch',
      adminFetch('Admin', { '/admin/stores': () => jsonResponse(paged([storeRow])) }),
    )

    renderWithProviders(<AdminStoresPage />, { route: '/admin/stores?status=Active' })

    await userEvent.click(await screen.findByRole('button', { name: 'Dayandır' }))

    const dialog = await screen.findByRole('dialog', { name: 'Mağazanı dayandır' })

    // D-5 is counter-intuitive, so the dialog states it in words rather than assuming it is known.
    expect(dialog).toHaveTextContent('Satıcının elanları isə saytda qalır')
    expect(within(dialog).getByRole('button', { name: 'Dayandır' })).toBeDisabled()
  })

  it('confirms an irreversible-looking action before sending it', async () => {
    const fetchMock = adminFetch('Admin', {
      '/admin/stores': () => jsonResponse(paged([{ ...storeRow, status: 'PendingVerification' }])),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<AdminStoresPage />, { route: '/admin/stores' })

    await userEvent.click(await screen.findByRole('button', { name: 'Təsdiqlə' }))

    const dialog = await screen.findByRole('dialog', { name: 'Ovçu Dünyası' })
    expect(dialog).toHaveTextContent('Mağaza saytda görünəcək')

    // Nothing is sent until the operator confirms.
    expect(
      fetchMock.mock.calls.some(([url]) => String(url).includes('/approve')),
    ).toBe(false)
  })
})

const category = {
  id: 1,
  parentId: null,
  slug: 'kamp',
  nameAz: 'Kamp',
  nameRu: 'Кемпинг',
  descriptionAz: 'Təsvir',
  metaTitleAz: 'Meta başlıq',
  metaDescriptionAz: 'Meta təsvir',
  iconKey: 'icon-key',
  imageKey: 'image-key',
  sortOrder: 10,
  depth: 0,
  isActive: true,
  isSelectable: false,
  isLeaf: false,
  restrictionStatus: 'Unrestricted',
  listingCount: 0,
  attributeCount: 0,
  children: [] as unknown[],
}

describe('AdminTaxonomyPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('sends every field back when only the name is edited', async () => {
    const fetchMock = adminFetch('Admin', {
      '/admin/categories': () => jsonResponse([category]),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<AdminTaxonomyPage />, { route: '/admin/taxonomy' })

    await userEvent.click(await screen.findByRole('button', { name: 'Kamp kateqoriyasını aç' }))

    const name = await screen.findByLabelText('Ad (AZ) *')
    await userEvent.clear(name)
    await userEvent.type(name, 'Yeni ad')
    await userEvent.click(screen.getByRole('button', { name: 'Yadda saxla' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).includes('/admin/categories/1') &&
          (init as RequestInit | undefined)?.method === 'PUT',
      )

      expect(call).toBeDefined()

      // The whole UpdateCategoryRequest: a partial body would blank the SEO fields server-side.
      expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({
        nameAz: 'Yeni ad',
        nameRu: 'Кемпинг',
        descriptionAz: 'Təsvir',
        metaTitleAz: 'Meta başlıq',
        metaDescriptionAz: 'Meta təsvir',
        iconKey: 'icon-key',
        imageKey: 'image-key',
        sortOrder: 10,
        isActive: true,
        isSelectable: false,
      })
    })
  })

  it('persists a whole sibling group when one category moves', async () => {
    const second = { ...category, id: 2, slug: 'baliqciliq', nameAz: 'Balıqçılıq', sortOrder: 20 }

    const fetchMock = adminFetch('Admin', {
      '/admin/categories/reorder': () => new Response(null, { status: 204 }),
      '/admin/categories': () => jsonResponse([category, second]),
    })

    vi.stubGlobal('fetch', fetchMock)
    renderWithProviders(<AdminTaxonomyPage />, { route: '/admin/taxonomy' })

    await userEvent.click(await screen.findByRole('button', { name: 'Balıqçılıq yuxarı' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).includes('/admin/categories/reorder'))

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).toEqual({
        items: [
          { id: 2, sortOrder: 10 },
          { id: 1, sortOrder: 20 },
        ],
      })
    })
  })

  it('confirms before changing a restriction, because it moves the public age gate', async () => {
    vi.stubGlobal('fetch', adminFetch('Admin', { '/admin/categories': () => jsonResponse([category]) }))

    renderWithProviders(<AdminTaxonomyPage />, { route: '/admin/taxonomy' })

    await userEvent.click(await screen.findByRole('button', { name: 'Kamp kateqoriyasını aç' }))
    await userEvent.selectOptions(await screen.findByLabelText('Təsnifat'), 'Restricted')

    const dialog = await screen.findByRole('dialog', { name: 'Təsnifatı dəyiş' })

    expect(dialog).toHaveTextContent('yaş təsdiqi')
  })
})

describe('AdminUsersPage inspect view', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  const person = {
    id: '44444444-4444-4444-4444-444444444444',
    phoneNumber: '+994501234567',
    fullName: 'Test İstifadəçi',
    email: null,
    role: 'User',
    isPhoneVerified: true,
    createdAt: '2026-02-01T10:00:00+00:00',
  }

  it('opens a read-only pane with no account actions on it', async () => {
    vi.stubGlobal(
      'fetch',
      adminFetch('Admin', {
        [`/users/${person.id}`]: () => jsonResponse(person),
        '/users': () => jsonResponse(paged([person])),
      }),
    )

    renderWithProviders(<AdminUsersPage />, { route: '/admin/users' })

    await userEvent.click(await screen.findByRole('button', { name: 'Aç' }))

    expect(await screen.findByText('yalnız baxış üçündür', { exact: false })).toBeInTheDocument()

    // PD-7.6: inspect-only. None of these may exist on the screen.
    for (const forbidden of [/blokla/i, /sil/i, /çıxış etdir/i, /şifrə/i]) {
      expect(screen.queryByRole('button', { name: forbidden })).not.toBeInTheDocument()
    }
  })
})
