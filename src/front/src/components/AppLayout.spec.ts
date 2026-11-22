import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import AppLayout from './AppLayout.vue'

async function mountLayout(path = '/') {
  const page = { template: '<p>page</p>' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        component: AppLayout,
        children: [
          { path: '', name: 'accueil', component: page },
          { path: 'documents', name: 'documents', component: page },
          { path: 'sessions', name: 'sessions', component: page },
        ],
      },
    ],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  await flushPromises()
  return { wrapper, router }
}

describe('AppLayout', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })

  it('affiche le menu, la page et les entrées à venir désactivées', async () => {
    const { wrapper } = await mountLayout('/documents')

    const active = wrapper.find('#menu-principal .nav-link.active')
    expect(active.text()).toBe('Documents')
    expect(wrapper.find('main').text()).toBe('page')
    const scanner = wrapper.findAll('.nav-link.disabled').find((l) => l.text().startsWith('Scanner'))!
    expect(scanner.attributes('aria-disabled')).toBe('true')
    expect(scanner.text()).toContain('bientôt')
  })

  it('lance une recherche plein texte vers la liste des documents', async () => {
    const { wrapper, router } = await mountLayout()

    await wrapper.find('form[role="search"] input').setValue(' facture edf ')
    await wrapper.find('form[role="search"]').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('documents')
    expect(router.currentRoute.value.query).toEqual({ q: 'facture edf', mode: 'avance', tri: 'pertinence' })
  })

  it('reprend la recherche en cours dans la barre', async () => {
    const { wrapper } = await mountLayout('/documents?q=edf')
    expect((wrapper.find('form[role="search"] input').element as HTMLInputElement).value).toBe('edf')
  })

  it('ouvre le tiroir sur mobile et le referme en changeant de page', async () => {
    const { wrapper, router } = await mountLayout()
    const drawer = () => wrapper.find('#menu-principal')

    await wrapper.find('button[aria-label="Ouvrir le menu"]').trigger('click')
    expect(drawer().classes()).toContain('show')
    expect(wrapper.find('.offcanvas-backdrop').exists()).toBe(true)

    await router.push('/documents')
    await flushPromises()
    expect(drawer().classes()).not.toContain('show')

    await wrapper.find('button[aria-label="Ouvrir le menu"]').trigger('click')
    await drawer().trigger('keydown', { key: 'Escape' })
    expect(drawer().classes()).not.toContain('show')
  })
})
