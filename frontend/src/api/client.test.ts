import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getAccessToken, setAccessToken } from '@/api/authToken'
import { ApiError, api, refreshSession } from '@/api/client'
import { fetchCall, jsonResponse, problemResponse } from '@/features/auth/authTestUtils'

describe('api client', () => {
  beforeEach(() => {
    setAccessToken(null)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    setAccessToken(null)
  })

  it('sends the access token as a bearer header when one is held', async () => {
    setAccessToken('token-123')
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await api.get('/users/me')

    const { headers } = fetchCall(fetchMock, 0)
    expect(headers.Authorization).toBe('Bearer token-123')
  })

  it('omits the authorization header when signed out', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await api.get('/categories')

    const { headers } = fetchCall(fetchMock, 0)
    expect(headers.Authorization).toBeUndefined()
  })

  it('exposes ProblemDetails field errors on failure', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        problemResponse(400, 'Göndərilən məlumatlar düzgün deyil.', {
          phoneNumber: ['Telefon nömrəsi düzgün deyil.'],
        }),
      ),
    )

    const error = await api.post('/auth/register', {}).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(400)
    expect(apiError.fieldError('phoneNumber')).toBe('Telefon nömrəsi düzgün deyil.')
    expect(apiError.fieldError('missing')).toBeUndefined()
  })

  it('collapses overlapping refreshes into a single request', async () => {
    // Refresh tokens rotate, so a second concurrent call presents a spent one — which the API
    // reads as reuse and answers by revoking every session the account has. Overlapping callers
    // must therefore share one call, not merely avoid wasting a request.
    let release: (value: Response) => void = () => {}
    const pending = new Promise<Response>((resolve) => {
      release = resolve
    })
    const fetchMock = vi.fn().mockReturnValue(pending)
    vi.stubGlobal('fetch', fetchMock)

    const first = refreshSession<{ accessToken: string }>()
    const second = refreshSession<{ accessToken: string }>()

    release(jsonResponse({ accessToken: 'rotated' }))

    expect(await first).toEqual({ accessToken: 'rotated' })
    expect(await second).toEqual({ accessToken: 'rotated' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(getAccessToken()).toBe('rotated')
  })

  it('starts a new refresh once the previous one has settled', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ accessToken: 'first' }))
      .mockResolvedValueOnce(jsonResponse({ accessToken: 'second' }))
    vi.stubGlobal('fetch', fetchMock)

    await refreshSession()
    await refreshSession()

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('reports a failed restore as null and clears the token', async () => {
    setAccessToken('stale')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problemResponse(401, 'Sessiya tapılmadı.')))

    expect(await refreshSession()).toBeNull()
    expect(getAccessToken()).toBeNull()
  })

  it('refreshes once on a 401 and replays the original request', async () => {
    setAccessToken('expired-token')

    const fetchMock = vi
      .fn()
      // 1. original request rejected
      .mockResolvedValueOnce(problemResponse(401, 'Giriş tələb olunur.'))
      // 2. refresh succeeds
      .mockResolvedValueOnce(jsonResponse({ accessToken: 'fresh-token', expiresInSeconds: 900 }))
      // 3. replay succeeds
      .mockResolvedValueOnce(jsonResponse({ id: 'user-1' }))

    vi.stubGlobal('fetch', fetchMock)

    const result = await api.get<{ id: string }>('/users/me')

    expect(result.id).toBe('user-1')
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchCall(fetchMock, 1).url).toContain('/auth/refresh')
    expect(getAccessToken()).toBe('fresh-token')

    // The replay carries the new token, not the expired one.
    const replayHeaders = fetchCall(fetchMock, 2).headers
    expect(replayHeaders.Authorization).toBe('Bearer fresh-token')
  })

  it('gives up and clears the token when the refresh itself fails', async () => {
    setAccessToken('expired-token')

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problemResponse(401, 'Giriş tələb olunur.'))
      .mockResolvedValueOnce(problemResponse(401, 'Sessiya tapılmadı.'))

    vi.stubGlobal('fetch', fetchMock)

    await expect(api.get('/users/me')).rejects.toBeInstanceOf(ApiError)

    // Two calls only: no retry storm.
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(getAccessToken()).toBeNull()
  })

  it('does not attempt a refresh when there was no session to begin with', async () => {
    const fetchMock = vi.fn().mockResolvedValue(problemResponse(401, 'Giriş tələb olunur.'))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api.get('/users/me')).rejects.toBeInstanceOf(ApiError)

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('returns undefined for a 204 response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(api.post('/auth/logout')).resolves.toBeUndefined()
  })
})
