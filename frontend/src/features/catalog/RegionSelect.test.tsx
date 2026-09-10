import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse, renderWithProviders } from '@/features/auth/authTestUtils'

import { RegionSelect } from './RegionSelect'
import type { Region } from './types'

function region(slug: string, nameAz: string): Region {
  return { id: slug.length, slug, nameAz, nameRu: null, listingCount: 0 }
}

/** Server order: pinned cities first, then the rest. The component must not re-sort. */
const regions = [
  region('baki', 'Bakı'),
  region('gence', 'Gəncə'),
  region('sumqayit', 'Sumqayıt'),
  region('masalli', 'Masallı'),
  region('quba', 'Quba'),
]

function mockRegions(response: () => Response) {
  vi.stubGlobal(
    'fetch',
    mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/regions': response,
    }),
  )
}

describe('RegionSelect', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('renders one flat list with no grouping', async () => {
    mockRegions(() => jsonResponse(regions))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} />)

    await waitFor(() => expect(screen.getByRole('option', { name: 'Bakı' })).toBeInTheDocument())

    const select = screen.getByRole('combobox')
    expect(select.querySelectorAll('optgroup')).toHaveLength(0)
    expect(screen.getByLabelText('Şəhər / rayon')).toBe(select)
  })

  it('preserves the server ordering', async () => {
    mockRegions(() => jsonResponse(regions))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} />)

    await waitFor(() => expect(screen.getByRole('option', { name: 'Bakı' })).toBeInTheDocument())

    const rendered = [...screen.getByRole('combobox').querySelectorAll('option')]
      .map((o) => o.textContent)
      .slice(1)

    expect(rendered).toEqual(['Bakı', 'Gəncə', 'Sumqayıt', 'Masallı', 'Quba'])
  })

  it('offers "all regions" in filter mode', async () => {
    mockRegions(() => jsonResponse(regions))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} />)

    // Wait for the data, otherwise the option is still inside a disabled select.
    await waitFor(() => expect(screen.getByRole('option', { name: 'Bakı' })).toBeInTheDocument())

    expect(screen.getByRole('option', { name: 'Bütün regionlar' })).toBeEnabled()
    expect(screen.getByRole('combobox')).not.toBeRequired()
  })

  it('is required and has no selectable empty option in listing mode', async () => {
    mockRegions(() => jsonResponse(regions))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} required />)

    await waitFor(() => expect(screen.getByRole('option', { name: 'Bakı' })).toBeInTheDocument())

    expect(screen.getByRole('combobox')).toBeRequired()
    expect(screen.queryByRole('option', { name: 'Bütün regionlar' })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Seçin' })).toBeDisabled()
  })

  it('reports the chosen slug', async () => {
    const onChange = vi.fn()
    mockRegions(() => jsonResponse(regions))
    renderWithProviders(<RegionSelect value="" onChange={onChange} />)

    await waitFor(() => expect(screen.getByRole('option', { name: 'Masallı' })).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByRole('combobox'), 'masalli')

    expect(onChange).toHaveBeenCalledWith('masalli')
  })

  it('reports that the list is not loaded when no region exists', async () => {
    // The real state until the authoritative dataset is imported.
    mockRegions(() => jsonResponse([]))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} />)

    expect(await screen.findByText('Region siyahısı hələ yüklənməyib.')).toBeInTheDocument()
    expect(screen.getByRole('combobox')).toBeDisabled()
  })

  it('surfaces a load failure', async () => {
    mockRegions(() => problemResponse(500, 'Server xətası.'))
    renderWithProviders(<RegionSelect value="" onChange={() => {}} />)

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Regionları yükləmək'))
  })
})
