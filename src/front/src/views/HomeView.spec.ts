import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import HomeView from './HomeView.vue'

describe('HomeView', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('affiche la version renvoyée par le BFF', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"version":"1.2.3"}')))

    const wrapper = mount(HomeView)
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).toContain('Version 1.2.3')
  })

  it("reste utilisable si le BFF ne répond pas", async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))

    const wrapper = mount(HomeView)
    await flushPromises()

    expect(wrapper.text()).toContain('Home-GED')
    expect(wrapper.text()).not.toContain('Version')
  })
})
