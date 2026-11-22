import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import HomeView from './HomeView.vue'

function mountHome() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: HomeView },
    ],
  })
  return mount(HomeView, { global: { plugins: [router] } })
}

describe('HomeView', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('affiche la version renvoyée par le BFF', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"version":"1.2.3"}')))

    const wrapper = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).toContain('Version 1.2.3')
  })

  it("reste utilisable si le BFF ne répond pas", async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))

    const wrapper = mountHome()
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).not.toContain('Version')
  })
})
