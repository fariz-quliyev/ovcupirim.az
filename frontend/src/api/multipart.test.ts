import { afterEach, describe, expect, it, vi } from 'vitest'

import { setAccessToken } from '@/api/authToken'
import { jsonResponse, mockFetchByUrl, problemResponse } from '@/features/auth/authTestUtils'
import { uploadListingMedia } from '@/features/listings/api'

/**
 * A multipart body has to reach the server with the boundary the browser generated. If the client
 * sets Content-Type itself the boundary is lost and the upload is rejected, so this pins that the
 * header is left alone for FormData and still set for JSON.
 */
describe('multipart requests', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('sends a file as FormData without overriding the content type', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/media': () => jsonResponse({ id: 'media-1', url: '/uploads/x.webp', variants: {} }, 201),
    })

    vi.stubGlobal('fetch', fetchMock)

    const file = new File([new Uint8Array([0xff, 0xd8, 0xff])], 'photo.jpg', { type: 'image/jpeg' })
    await uploadListingMedia('11111111-1111-1111-1111-111111111111', file)

    const [, init] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    const headers = init.headers as Record<string, string>

    expect(init.body).toBeInstanceOf(FormData)
    expect((init.body as FormData).get('file')).toBe(file)
    expect(headers['Content-Type']).toBeUndefined()
  })

  it('still sets the JSON content type for an ordinary body', async () => {
    const fetchMock = mockFetchByUrl({
      '/auth/refresh': () => problemResponse(401, 'Sessiya tapılmadı.'),
      '/listings': () => jsonResponse({}, 201),
    })

    vi.stubGlobal('fetch', fetchMock)

    const { api } = await import('@/api/client')
    await api.post('/listings/1/publish', { ageConfirmed: false })

    const [, init] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    const headers = init.headers as Record<string, string>

    expect(headers['Content-Type']).toBe('application/json')
  })
})
