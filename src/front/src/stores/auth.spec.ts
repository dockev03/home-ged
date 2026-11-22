import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { memoryBackend, offlineDb, setOfflineBackend } from '../offline/db'
import { lastUser, rememberUser } from '../offline/session'
import { REVALIDATE_MS, useAuthStore } from './auth'

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

describe('store auth hors ligne (PWA-09)', () => {
  const ALICE = { id: 1, username: 'alice' }

  beforeEach(() => {
    setOfflineBackend(memoryBackend())
    setActivePinia(createPinia())
  })
  afterEach(() => {
    setOfflineBackend(null)
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('retient l’utilisateur connecté sur l’appareil', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(ALICE)))
    await useAuthStore().ensureLoaded()
    await flushPromises()
    expect(await lastUser()).toEqual(ALICE)
  })

  it('démarre hors ligne avec le dernier utilisateur de l’appareil', async () => {
    await rememberUser(ALICE)
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))
    const auth = useAuthStore()

    await auth.ensureLoaded()

    expect(auth.user).toEqual(ALICE)
    expect(auth.offline).toBe(true)
  })

  it('sans utilisateur retenu, hors ligne, l’erreur remonte (écran de connexion)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))
    await expect(useAuthStore().ensureLoaded()).rejects.toThrow(TypeError)
  })

  it('revérifie la session au retour du réseau ; expirée, fin de session et données effacées', async () => {
    vi.useFakeTimers()
    await rememberUser(ALICE)
    await offlineDb.put('documents', { key: '4' })
    await offlineDb.put('envois', { key: 'e1' })
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('réseau'))
    vi.stubGlobal('fetch', fetchMock)
    const auth = useAuthStore()
    const lost = vi.fn()
    auth.onSessionLost(lost)
    await auth.ensureLoaded()

    // Toujours injoignable au nouvel essai périodique : on reste hors ligne.
    await vi.advanceTimersByTimeAsync(REVALIDATE_MS)
    expect(auth.offline).toBe(true)

    fetchMock.mockResolvedValue(new Response('{}', { status: 401 }))
    window.dispatchEvent(new Event('online'))
    await flushPromises()

    expect(auth.user).toBeNull()
    expect(auth.offline).toBe(false)
    expect(lost).toHaveBeenCalledOnce()
    expect(await offlineDb.getAll('documents')).toEqual([])
    expect((await offlineDb.getAll('envois')).map((r) => r.key)).toEqual(['e1'])
  })

  it('au retour du réseau avec une session valide, repasse en ligne', async () => {
    await rememberUser(ALICE)
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('réseau'))
    vi.stubGlobal('fetch', fetchMock)
    const auth = useAuthStore()
    await auth.ensureLoaded()

    fetchMock.mockResolvedValue(Response.json(ALICE))
    await auth.revalidate()

    expect(auth.offline).toBe(false)
    expect(auth.user).toEqual(ALICE)
  })

  it('la déconnexion efface tout ce qui est gardé sur l’appareil', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(Response.json(ALICE)).mockResolvedValueOnce(new Response(null, { status: 204 })))
    const auth = useAuthStore()
    await auth.login({ username: 'alice', password: 'secret' })
    await flushPromises()
    await offlineDb.put('envois', { key: 'e1' })

    await auth.logout()
    await flushPromises()

    expect(await offlineDb.getAll('envois')).toEqual([])
    expect(await lastUser()).toBeNull()
  })
})
