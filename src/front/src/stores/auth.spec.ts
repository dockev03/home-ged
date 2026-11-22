import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'

describe('store auth', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('vérifie la session une seule fois', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{"id":1,"username":"alice"}'))
    vi.stubGlobal('fetch', fetchMock)
    const auth = useAuthStore()

    await auth.ensureLoaded()
    await auth.ensureLoaded()

    expect(auth.user).toEqual({ id: 1, username: 'alice' })
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('sans session, aucun utilisateur', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))
    const auth = useAuthStore()

    await auth.ensureLoaded()

    expect(auth.isAuthenticated).toBe(false)
  })

  it("oublie l'utilisateur à la déconnexion, même si le BFF échoue", async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response('{"id":1,"username":"alice"}'))
        .mockRejectedValueOnce(new TypeError('réseau')),
    )
    const auth = useAuthStore()
    await auth.login({ username: 'alice', password: 'secret' })
    expect(auth.isAuthenticated).toBe(true)

    await expect(auth.logout()).rejects.toThrow()
    expect(auth.isAuthenticated).toBe(false)
  })
})
