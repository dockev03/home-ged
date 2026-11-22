import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import HomeView from './HomeView.vue'

function mountHome() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: HomeView },
      { path: '/connexion', name: 'connexion', component: { template: '<p>connexion</p>' } },
    ],
  })
  return { wrapper: mount(HomeView, { global: { plugins: [router] } }), router }
}

describe('HomeView', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('affiche la version renvoyée par le BFF', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"version":"1.2.3"}')))

    const { wrapper } = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).toContain('Version 1.2.3')
  })

  it("reste utilisable si le BFF ne répond pas", async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))

    const { wrapper } = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).not.toContain('Version')
  })

  it("affiche l'utilisateur et le déconnecte", async () => {
    const fetchMock = vi.fn().mockImplementation((path: string) =>
      Promise.resolve(path === '/api/auth/logout' ? new Response(null, { status: 204 }) : new Response('{"version":"1"}')),
    )
    vi.stubGlobal('fetch', fetchMock)
    useAuthStore().user = { id: 1, username: 'alice' }

    const { wrapper, router } = mountHome()
    await flushPromises()
    expect(wrapper.text()).toContain('alice')

    await wrapper.findAll('button').find((b) => b.text() === 'Se déconnecter')!.trigger('click')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/logout', expect.objectContaining({ method: 'POST' }))
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(router.currentRoute.value.path).toBe('/connexion')
  })
})
