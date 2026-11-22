import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore } from '../stores/preferences'
import AppHeader from './AppHeader.vue'

async function mountHeader(props: { retour?: boolean } = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'accueil', component: { template: '<p>accueil</p>' } },
      { path: '/sessions', name: 'sessions', component: { template: '<p>sessions</p>' } },
      { path: '/connexion', name: 'connexion', component: { template: '<p>connexion</p>' } },
    ],
  })
  await router.push('/sessions')
  const wrapper = mount(AppHeader, { props, global: { plugins: [router] }, attachTo: document.body })
  return { wrapper, router }
}

/** Ouvre le menu (rendu dans <body> par Naive UI) et choisit une entrée par son libellé. */
async function choose(wrapper: Awaited<ReturnType<typeof mountHeader>>['wrapper'], label: string) {
  await wrapper.find('button.menu').trigger('click')
  await flushPromises()
  const option = [...document.querySelectorAll<HTMLElement>('.n-dropdown-option-body')].find(
    (el) => el.textContent?.trim().endsWith(label),
  )
  expect(option, `entrée « ${label} »`).toBeDefined()
  option!.click()
  await flushPromises()
}

describe('AppHeader', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it("affiche l'utilisateur et le retour à l'accueil sur demande", async () => {
    const { wrapper, router } = await mountHeader({ retour: true })
    expect(wrapper.text()).toContain('alice')

    await wrapper.findAll('button').find((b) => b.text().includes('Accueil'))!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('accueil')
  })

  it("n'affiche pas le retour sur l'accueil", async () => {
    const { wrapper } = await mountHeader()
    expect(wrapper.text()).not.toContain('Accueil')
  })

  it('change et mémorise le thème', async () => {
    const { wrapper } = await mountHeader()

    await choose(wrapper, 'Sombre')

    expect(usePreferencesStore().theme).toBe('sombre')
    expect(localStorage.getItem('homeged.theme')).toBe('sombre')
  })

  it('ouvre « Mes sessions »', async () => {
    const { wrapper, router } = await mountHeader()
    await router.push('/')

    await choose(wrapper, 'Mes sessions')

    expect(router.currentRoute.value.name).toBe('sessions')
  })

  it('déconnecte, même si le BFF ne répond pas', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('réseau'))
    vi.stubGlobal('fetch', fetchMock)
    const { wrapper, router } = await mountHeader()

    await choose(wrapper, 'Se déconnecter')

    expect(fetchMock).toHaveBeenCalledWith('/api/auth/logout', expect.objectContaining({ method: 'POST' }))
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(router.currentRoute.value.name).toBe('connexion')
  })
})
