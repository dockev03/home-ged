import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore } from '../stores/preferences'
import UserMenu from './UserMenu.vue'

async function mountMenu() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'accueil', component: { template: '<p>accueil</p>' } },
      { path: '/sessions', name: 'sessions', component: { template: '<p>sessions</p>' } },
      { path: '/connexion', name: 'connexion', component: { template: '<p>connexion</p>' } },
    ],
  })
  await router.push('/')
  const wrapper = mount(UserMenu, { global: { plugins: [router] }, attachTo: document.body })
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountMenu>>['wrapper']

/** Ouvre le menu et choisit une entrée par son libellé. */
async function choose(wrapper: Wrapper, label: string) {
  await wrapper.find('button.dropdown-toggle').trigger('click')
  const item = wrapper.findAll('.dropdown-item').find((el) => el.text() === label)
  expect(item, `entrée « ${label} »`).toBeDefined()
  await item!.trigger('click')
  await flushPromises()
}

describe('UserMenu', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it("affiche l'utilisateur, menu fermé", async () => {
    const { wrapper } = await mountMenu()
    expect(wrapper.text()).toContain('alice')
    expect(wrapper.find('.dropdown-menu').exists()).toBe(false)
    expect(wrapper.find('button.dropdown-toggle').attributes('aria-expanded')).toBe('false')
  })

  it('change et mémorise le thème, coche annoncée', async () => {
    const { wrapper } = await mountMenu()

    await choose(wrapper, 'Sombre')

    expect(usePreferencesStore().theme).toBe('sombre')
    expect(localStorage.getItem('homeged.theme')).toBe('sombre')
    const sombre = wrapper.findAll('[role="menuitemradio"]').find((el) => el.text() === 'Sombre')!
    expect(sombre.attributes('aria-checked')).toBe('true')
  })

  it('se ferme sur Échap et au clic extérieur', async () => {
    const { wrapper } = await mountMenu()
    await wrapper.find('button.dropdown-toggle').trigger('click')
    expect(wrapper.find('.dropdown-menu').exists()).toBe(true)

    await wrapper.find('.dropdown-menu').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('.dropdown-menu').exists()).toBe(false)

    await wrapper.find('button.dropdown-toggle').trigger('click')
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    await flushPromises()
    expect(wrapper.find('.dropdown-menu').exists()).toBe(false)
  })

  it('ouvre « Mes sessions »', async () => {
    const { wrapper, router } = await mountMenu()

    await choose(wrapper, 'Mes sessions')

    expect(router.currentRoute.value.name).toBe('sessions')
    expect(wrapper.find('.dropdown-menu').exists()).toBe(false)
  })

  it('déconnecte, même si le BFF ne répond pas', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('réseau'))
    vi.stubGlobal('fetch', fetchMock)
    const { wrapper, router } = await mountMenu()

    await choose(wrapper, 'Se déconnecter')

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/logout', expect.objectContaining({ method: 'POST' }))
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(router.currentRoute.value.name).toBe('connexion')
  })
})
