import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { SessionInfo } from '../api/sessions'
import { useAuthStore } from '../stores/auth'
import SessionsView from './SessionsView.vue'

const FIREFOX_ANDROID = 'Mozilla/5.0 (Android 15; Mobile; rv:140.0) Gecko/140.0 Firefox/140.0'
const CHROME_WINDOWS =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36'

const session = (id: string, current: boolean, lastSeenAt: string, userAgent: string | null): SessionInfo => ({
  id,
  createdAt: '2026-11-01T08:00:00Z',
  lastSeenAt,
  expiresAt: '2026-12-22T08:00:00Z',
  ip: current ? '192.0.2.10' : '198.51.100.7',
  userAgent,
  current,
})

const SESSIONS = [
  session('aaaaaaaa-0000-0000-0000-000000000001', false, '2026-11-20T08:00:00Z', CHROME_WINDOWS),
  session('aaaaaaaa-0000-0000-0000-000000000002', true, '2026-11-22T08:00:00Z', FIREFOX_ANDROID),
]

const json = (body: unknown) => new Response(JSON.stringify(body))
const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ status, title, code }), { status })

async function mountSessions() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'accueil', component: { template: '<p>accueil</p>' } },
      { path: '/sessions', name: 'sessions', component: SessionsView },
      { path: '/connexion', name: 'connexion', component: { template: '<p>connexion</p>' } },
    ],
  })
  await router.push('/sessions')
  const wrapper = mount(SessionsView, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountSessions>>['wrapper']

const card = (wrapper: Wrapper, id: string) => wrapper.find(`[data-session="${id}"]`)

async function click(wrapper: Wrapper, id: string, label: string) {
  const button = card(wrapper, id)
    .findAll('button')
    .find((b) => b.text().startsWith(label))
  expect(button, `bouton « ${label} »`).toBeDefined()
  await button!.trigger('click')
  await flushPromises()
}

describe('SessionsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it("liste les sessions, cet appareil d'abord, avec un libellé lisible", async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(SESSIONS)))

    const { wrapper } = await mountSessions()

    const cards = wrapper.findAll('[data-session]')
    expect(cards).toHaveLength(2)
    expect(cards[0]!.text()).toContain('Firefox sur Android')
    expect(cards[0]!.text()).toContain('Cet appareil')
    expect(cards[0]!.text()).toContain('192.0.2.10')
    expect(cards[1]!.text()).toContain('Chrome sur Windows')
    expect(cards[1]!.text()).not.toContain('Cet appareil')
  })

  it('révoque une autre session après confirmation', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json(SESSIONS))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const { wrapper, router } = await mountSessions()
    const other = SESSIONS[0]!.id

    await click(wrapper, other, 'Révoquer')
    expect(fetchMock).toHaveBeenCalledOnce()
    await click(wrapper, other, 'Confirmer')

    expect(fetchMock).toHaveBeenLastCalledWith(
      `/api/auth/sessions/${other}`,
      expect.objectContaining({ method: 'DELETE', headers: expect.objectContaining({ 'X-Requested-With': 'HomeGed' }) }),
    )
    expect(wrapper.findAll('[data-session]')).toHaveLength(1)
    expect(useAuthStore().isAuthenticated).toBe(true)
    expect(router.currentRoute.value.name).toBe('sessions')
  })

  it("n'appelle pas le BFF si la révocation est annulée", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json(SESSIONS))
    vi.stubGlobal('fetch', fetchMock)
    const { wrapper } = await mountSessions()
    const other = SESSIONS[0]!.id

    await click(wrapper, other, 'Révoquer')
    await click(wrapper, other, 'Annuler')

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(card(wrapper, other).text()).toContain('Révoquer')
  })

  it('révoquer cette session déconnecte', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(json(SESSIONS))
        .mockResolvedValueOnce(new Response(null, { status: 204 })),
    )
    const { wrapper, router } = await mountSessions()
    const current = SESSIONS[1]!.id

    await click(wrapper, current, 'Révoquer')
    expect(card(wrapper, current).text()).toContain('Cet appareil sera déconnecté.')
    await click(wrapper, current, 'Confirmer')

    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(router.currentRoute.value.name).toBe('connexion')
  })

  it('une session déjà disparue est retirée de la liste', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(json(SESSIONS))
        .mockResolvedValueOnce(problem(404, 'Session introuvable.', 'session_introuvable')),
    )
    const { wrapper } = await mountSessions()
    const other = SESSIONS[0]!.id

    await click(wrapper, other, 'Révoquer')
    await click(wrapper, other, 'Confirmer')

    expect(wrapper.findAll('[data-session]')).toHaveLength(1)
    expect(wrapper.find('.n-alert').exists()).toBe(false)
  })

  it("affiche l'erreur et garde la session si la révocation échoue", async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(json(SESSIONS))
        .mockRejectedValueOnce(new TypeError('réseau')),
    )
    const { wrapper } = await mountSessions()
    const other = SESSIONS[0]!.id

    await click(wrapper, other, 'Révoquer')
    await click(wrapper, other, 'Confirmer')

    expect(wrapper.text()).toContain('Impossible de joindre Home-GED')
    expect(wrapper.findAll('[data-session]')).toHaveLength(2)
  })

  it('propose de réessayer si la liste ne se charge pas', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockRejectedValueOnce(new TypeError('réseau'))
        .mockResolvedValueOnce(json(SESSIONS)),
    )
    const { wrapper } = await mountSessions()
    expect(wrapper.text()).toContain('Impossible de joindre Home-GED')

    await wrapper.findAll('button').find((b) => b.text() === 'Réessayer')!.trigger('click')
    await flushPromises()

    expect(wrapper.findAll('[data-session]')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('Impossible de joindre')
  })
})
