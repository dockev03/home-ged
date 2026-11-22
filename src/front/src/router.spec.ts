import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { router, safeRedirect } from './router'

describe('router', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it("renvoie vers la connexion sans session, en gardant la page demandée", async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))

    await router.push('/introuvable?x=1')

    expect(router.currentRoute.value.name).toBe('connexion')
    expect(router.currentRoute.value.query.suite).toBe('/introuvable?x=1')
  })

  it("laisse passer un utilisateur connecté et l'écarte de l'écran de connexion", async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"id":1,"username":"alice"}')))

    await router.push('/connexion?suite=/autre')

    expect(router.currentRoute.value.path).toBe('/autre')
  })

  it('ouvre la page publique d’un lien sans session', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })))

    await router.push('/s/AAAAAAAAAAAAAAAAAAAAAA')

    expect(router.currentRoute.value.name).toBe('partage-public')
    expect(router.currentRoute.value.params.id).toBe('AAAAAAAAAAAAAAAAAAAAAA')
  })

  it.each([
    ['/documents/3', '/documents/3'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
    ['https://evil.example', '/'],
    [undefined, '/'],
  ])('safeRedirect(%s) = %s', (value, expected) => {
    expect(safeRedirect(value)).toBe(expected)
  })
})
