import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'
import type { CategorySchema } from '@/features/catalog/types'

import { ListingForm } from './ListingForm'
import type { ListingDetail } from './types'

const schema: CategorySchema = {
  category: {
    id: 37,
    slug: 'bel-cantasi',
    nameAz: 'Bel çantası',
    path: [{ id: 35, slug: 'canta-ve-aksesuar', nameAz: 'Çanta və aksesuar' }],
    restrictionStatus: 'Unrestricted',
    requiresAgeConfirmation: false,
    isSelectable: true,
    isLeaf: true,
  },
  attributes: [
    {
      key: 'capacity_l',
      labelAz: 'Həcm',
      labelRu: null,
      dataType: 'Number',
      unit: 'l',
      isRequired: true,
      isFilterable: true,
      isSearchable: false,
      minValue: 1,
      maxValue: 200,
      decimalPlaces: 0,
      maxLength: null,
      placeholderAz: null,
      helpTextAz: null,
      inherited: false,
      sortOrder: 10,
      options: null,
    },
  ],
}

const regions = [{ id: 13, slug: 'baki', nameAz: 'Bakı', nameRu: null, listingCount: 0 }]

const draft: ListingDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  shortId: 2001,
  slug: 'ov-bel-cantasi',
  status: 'Draft',
  rejectionReason: null,
  categoryId: 37,
  categorySlug: 'bel-cantasi',
  categoryNameAz: 'Bel çantası',
  categoryPath: [],
  regionId: 13,
  regionSlug: 'baki',
  regionNameAz: 'Bakı',
  title: 'Ov bel çantası 30L',
  description: 'Az istifadə olunub.',
  price: 150,
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
  createdAt: '2026-09-02T09:00:00+00:00',
  updatedAt: null,
  can: { edit: true, publish: true, delete: true, restore: false, markSold: false },
}

function baseRoutes(extra: Record<string, () => Response> = {}) {
  return {
    '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
    '/regions': () => jsonResponse(regions),
    ...extra,
  }
}

describe('ListingForm', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders the sections in one page rather than as wizard steps', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await screen.findByRole('heading', { name: 'Əsas məlumat' })

    for (const section of ['Əsas məlumat', 'Xüsusiyyətlər', 'Qiymət və vəziyyət', 'Şəkillər', 'Yer və əlaqə']) {
      expect(screen.getByRole('heading', { name: section })).toBeInTheDocument()
    }
  })

  it('renders the category schema without knowing what the field means', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    expect(await screen.findByLabelText('Həcm, l *')).toBeInTheDocument()
  })

  it('prefills the contact number from the profile', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    expect(await screen.findByLabelText('Əlaqə nömrəsi *')).toHaveValue('+994501234567')
  })

  it('lands a server field error on the attribute it belongs to', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl(
        baseRoutes({
          '/listings': () =>
            problemResponse(400, 'Məlumatlar düzgün deyil.', {
              'attributes.capacity_l': ['Bu sahə tələb olunur.'],
            }),
        }),
      ),
    )

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Qaralamanı yadda saxla' }))

    await waitFor(() => expect(screen.getByLabelText('Həcm, l *')).toHaveAttribute('aria-invalid', 'true'))
    expect(screen.getByRole('alert')).toHaveTextContent('Bu sahə tələb olunur.')
  })

  it('lands a region error on the location field', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl(
        baseRoutes({
          '/listings': () =>
            problemResponse(400, 'Məlumatlar düzgün deyil.', { regionSlug: ['Region tapılmadı.'] }),
        }),
      ),
    )

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Qaralamanı yadda saxla' }))

    expect(await screen.findByText('Region tapılmadı.')).toBeInTheDocument()
  })

  it('sends an empty price as null, which the site shows as negotiable', async () => {
    const fetchMock = mockFetchByUrl(baseRoutes({ '/listings': () => jsonResponse(draft, 201) }))
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Qaralamanı yadda saxla' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).includes('/listings'))

      expect(call).toBeDefined()

      const body = JSON.parse(String((call![1] as RequestInit).body)) as { price: number | null }

      expect(body.price).toBeNull()
    })
  })

  it('opens the image section only once the draft exists', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes({ '/listings': () => jsonResponse(draft, 201) })))

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    expect(await screen.findByText(/əvvəlcə qaralamanı yadda saxlayın/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Qaralamanı yadda saxla' }))

    expect(await screen.findByRole('button', { name: 'Şəkil əlavə et' })).toBeInTheDocument()
  })

  it('refuses to publish a listing that has no image', async () => {
    const fetchMock = mockFetchByUrl(baseRoutes({ '/listings': () => jsonResponse(draft, 201) }))
    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    // The browser's own required-field check has to pass first, or the form never submits.
    await userEvent.type(await screen.findByLabelText('Başlıq *'), 'Ov bel çantası 30L')
    await userEvent.type(screen.getByLabelText('Təsvir *'), 'Az istifadə olunub.')
    await userEvent.type(screen.getByLabelText('Həcm, l *'), '30')
    await screen.findByRole('option', { name: 'Bakı' })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Şəhər / rayon' }), 'baki')

    await userEvent.click(screen.getByRole('button', { name: 'Elanı yerləşdir' }))

    expect(await screen.findByText('Ən azı bir şəkil əlavə edin.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/publish'))).toBe(false)
  })

  it('freezes title, condition and phone once the listing is live', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    renderWithProviders(
      <ListingForm
        schema={schema}
        existing={{ ...draft, status: 'Active' }}
        defaultPhone="+994501234567"
        onPublished={() => {}}
      />,
    )

    expect(await screen.findByLabelText('Başlıq *')).toBeDisabled()
    expect(screen.getByLabelText('Vəziyyət')).toBeDisabled()
    expect(screen.getByLabelText('Əlaqə nömrəsi *')).toBeDisabled()

    // What Tap.az still allows after publication.
    expect(screen.getByLabelText('Təsvir *')).toBeEnabled()
    expect(screen.getByLabelText('Qiymət, ₼')).toBeEnabled()
    expect(screen.getByRole('checkbox', { name: 'Çatdırılma var' })).toBeEnabled()
  })

  it('blocks publishing a restricted category until the age box is ticked', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    const restricted: CategorySchema = {
      ...schema,
      category: { ...schema.category, restrictionStatus: 'Restricted', requiresAgeConfirmation: true },
    }

    renderWithProviders(
      <ListingForm schema={restricted} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    expect(await screen.findByRole('button', { name: 'Elanı yerləşdir' })).toBeDisabled()

    await userEvent.click(screen.getByRole('checkbox', { name: /18 yaşım tamam olub/ }))

    expect(screen.getByRole('button', { name: 'Elanı yerləşdir' })).toBeEnabled()
  })

  it('presents an unclassified category as pending classification, not as restricted', async () => {
    vi.stubGlobal('fetch', mockFetchByUrl(baseRoutes()))

    const unclassified: CategorySchema = {
      ...schema,
      category: { ...schema.category, restrictionStatus: 'Unclassified', requiresAgeConfirmation: true },
    }

    renderWithProviders(
      <ListingForm schema={unclassified} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    expect(await screen.findByText(/təsnifat gözlənilir/)).toBeInTheDocument()
  })
})

/**
 * Filing a listing under a storefront. The toggle is offered only when the caller actually owns a
 * live store, it is sent only on creation, and the server derives sellerType from the outcome.
 */
describe('ListingForm and storefronts', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  const activeStore = {
    id: '22222222-2222-2222-2222-222222222222',
    slug: 'ovcu-dunyasi',
    name: 'Ovçu Dünyası',
    description: null,
    address: null,
    phone: null,
    status: 'Active',
    isVerified: false,
    isPublic: true,
    logoUrl: null,
    bannerUrl: null,
    listingCount: 3,
    followerCount: 1,
    createdAt: '2026-01-15T10:00:00+00:00',
  }

  it('offers nothing to a seller with no storefront', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl(baseRoutes({ '/me/store': () => problemResponse(404, 'Mağazanız yoxdur.') })),
    )

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await screen.findByRole('heading', { name: 'Əsas məlumat' })

    expect(screen.queryByRole('heading', { name: 'Satıcı' })).not.toBeInTheDocument()
  })

  it('offers nothing while the application is still awaiting approval', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl(
        baseRoutes({
          '/me/store': () => jsonResponse({ ...activeStore, status: 'PendingVerification', isPublic: false }),
        }),
      ),
    )

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await screen.findByRole('heading', { name: 'Əsas məlumat' })

    expect(screen.queryByRole('heading', { name: 'Satıcı' })).not.toBeInTheDocument()
  })

  it('sends useStore only when the owner asks for it', async () => {
    const fetchMock = mockFetchByUrl(
      baseRoutes({
        '/me/store': () => jsonResponse(activeStore),
        '/listings': () => jsonResponse({ ...draft, sellerType: 'Store' }),
      }),
    )

    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    const toggle = await screen.findByLabelText('Elan "Ovçu Dünyası" mağazasında yerləşdirilsin')
    await userEvent.click(toggle)
    await userEvent.click(screen.getByRole('button', { name: 'Qaralamanı yadda saxla' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith('/listings') && (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).toMatchObject({ useStore: true })
    })
  })

  it('leaves useStore out entirely when the toggle stays off', async () => {
    const fetchMock = mockFetchByUrl(
      baseRoutes({
        '/me/store': () => jsonResponse(activeStore),
        '/listings': () => jsonResponse(draft),
      }),
    )

    vi.stubGlobal('fetch', fetchMock)

    renderWithProviders(
      <ListingForm schema={schema} defaultPhone="+994501234567" onPublished={() => {}} />,
    )

    await screen.findByLabelText('Elan "Ovçu Dünyası" mağazasında yerləşdirilsin')
    await userEvent.click(screen.getByRole('button', { name: 'Qaralamanı yadda saxla' }))

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(
        ([url, init]) =>
          String(url).endsWith('/listings') && (init as RequestInit | undefined)?.method === 'POST',
      )

      expect(call).toBeDefined()
      expect(JSON.parse(String((call![1] as RequestInit).body))).not.toHaveProperty('useStore')
    })
  })

  it('states the link rather than offering it when editing an existing store listing', async () => {
    vi.stubGlobal(
      'fetch',
      mockFetchByUrl(baseRoutes({ '/me/store': () => jsonResponse(activeStore) })),
    )

    renderWithProviders(
      <ListingForm
        schema={schema}
        existing={{ ...draft, sellerType: 'Store' }}
        defaultPhone="+994501234567"
        onPublished={() => {}}
      />,
    )

    expect(await screen.findByText('Bu elan mağazanıza bağlıdır.')).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Elan "Ovçu Dünyası" mağazasında yerləşdirilsin'),
    ).not.toBeInTheDocument()
  })
})

