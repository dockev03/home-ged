import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getJson } from './http'

describe('getJson', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renvoie le JSON et envoie les cookies de même origine', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{"version":"1.0.0"}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(getJson('/api/info')).resolves.toEqual({ version: '1.0.0' })
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/info',
      expect.objectContaining({ credentials: 'same-origin', headers: { Accept: 'application/json' } }),
    )
  })

  it('lève une ApiError avec le code HTTP', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })))

    const error = await getJson('/api/info').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(503)
  })
})
