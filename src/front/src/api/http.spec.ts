import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getJson, onUnauthorized, sendJson } from './http'

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ status, title, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })

describe('getJson', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    onUnauthorized(null)
  })

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

  it('reprend le titre et le code du « problem » renvoyé par le BFF', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(429, 'Trop de tentatives.', 'trop_de_tentatives')))

    const error = (await getJson('/api/x').catch((e: unknown) => e)) as ApiError
    expect(error.message).toBe('Trop de tentatives.')
    expect(error.code).toBe('trop_de_tentatives')
  })

  it('prévient le gestionnaire sur un 401, sauf si demandé', async () => {
    const handler = vi.fn()
    onUnauthorized(handler)
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(problem(401, 'Connexion requise.', 'non_connecte'))))

    await getJson('/api/x', { ignoreUnauthorized: true }).catch(() => undefined)
    expect(handler).not.toHaveBeenCalled()

    await getJson('/api/x').catch(() => undefined)
    expect(handler).toHaveBeenCalledOnce()
  })
})

describe('sendJson', () => {
  afterEach(() => vi.unstubAllGlobals())

  it("envoie le corps JSON et l'en-tête anti-CSRF", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{"ok":true}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(sendJson('POST', '/api/x', { a: 1 })).resolves.toEqual({ ok: true })
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(init.body).toBe('{"a":1}')
    expect(init.headers).toMatchObject({ 'X-Requested-With': 'HomeGed', 'Content-Type': 'application/json' })
  })

  it('renvoie null sur une réponse 204', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))
    await expect(sendJson('DELETE', '/api/x')).resolves.toBeNull()
  })
})
