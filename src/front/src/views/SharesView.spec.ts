import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { ShareLink } from '../api/shares'
import SharesView from './SharesView.vue'

const LINK_A = 'AAAAAAAAAAAAAAAAAAAAAA'
const LINK_B = 'BBBBBBBBBBBBBBBBBBBBBB'

const link = (id: string, extra: Partial<ShareLink> = {}): ShareLink => ({
  id,
  document: 5,
  titre: 'Bail',
  creeLe: '2026-11-20T08:00:00Z',
  expireLe: '2026-11-27T08:00:00Z',
  telechargementsMax: null,
  telechargements: 0,
  ...extra,
})

interface Call {
  method: string
  path: string
}

function stubBff(links: ShareLink[], revokeStatus = 204) {
  const calls: Call[] = []
  let current = [...links]
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const method = init?.method ?? 'GET'
      calls.push({ method, path: url.pathname })
      if (method === 'DELETE') {
        if (revokeStatus !== 204) return Response.json({ title: 'Erreur serveur.' }, { status: revokeStatus })
        current = current.filter((l) => !url.pathname.endsWith(l.id))
        return new Response(null, { status: 204 })
      }
      return Response.json({ results: current })
    }),
  )
  return calls
}

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/liens', name: 'liens', component: SharesView },
      { path: '/documents/:id', name: 'document', component: { template: '<p>fiche</p>' } },
    ],
  })
  await router.push('/liens')
  const wrapper = mount(SharesView, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return wrapper
}

type Wrapper = Awaited<ReturnType<typeof mountView>>

const button = (wrapper: Wrapper, label: string, within = '') => {
  const found = wrapper.findAll(`${within} button`).find((b) => b.text() === label)
  expect(found, `bouton « ${label} »`).toBeDefined()
  return found!
}

describe('SharesView', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-22T08:00:00Z'))
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('liste les liens actifs avec leur document, leur échéance et leurs téléchargements', async () => {
    stubBff([
      link(LINK_A, { telechargementsMax: 3, telechargements: 3 }),
      link(LINK_B, { titre: null, document: 9, telechargements: 1 }),
    ])
    const wrapper = await mountView()

    const a = wrapper.find(`[data-lien="${LINK_A}"]`)
    expect(a.find('a').attributes('href')).toBe('/documents/5')
    expect(a.text()).toContain('Bail')
    expect(a.text()).toContain('expire dans 5 jours')
    expect(a.text()).toContain('3 / 3 téléchargements')
    expect(a.text()).toContain('épuisé')

    const b = wrapper.find(`[data-lien="${LINK_B}"]`)
    expect(b.find('a').exists()).toBe(false)
    expect(b.text()).toContain('Document n° 9')
    expect(b.text()).toContain('inaccessible')
    expect(b.text()).toContain('1 téléchargement')
    expect(b.text()).not.toContain('épuisé')
  })

  it('indique l’absence de lien', async () => {
    stubBff([])
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Aucun lien actif.')
  })

  it('révoque un lien après confirmation et recharge la liste', async () => {
    const calls = stubBff([link(LINK_A), link(LINK_B)])
    const wrapper = await mountView()

    await button(wrapper, 'Révoquer', `[data-lien="${LINK_A}"]`).trigger('click')
    expect(calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
    expect(wrapper.text()).toContain('Révoquer ce lien ?')

    await button(wrapper, 'Révoquer', '.modal').trigger('click')
    await flushPromises()

    expect(calls.filter((c) => c.method === 'DELETE')).toEqual([{ method: 'DELETE', path: `/api/partages/${LINK_A}` }])
    expect(wrapper.find(`[data-lien="${LINK_A}"]`).exists()).toBe(false)
    expect(wrapper.find('[role="status"]').text()).toContain('révoqué')
  })

  it('annuler la confirmation ne révoque rien', async () => {
    const calls = stubBff([link(LINK_A)])
    const wrapper = await mountView()

    await button(wrapper, 'Révoquer', `[data-lien="${LINK_A}"]`).trigger('click')
    await button(wrapper, 'Annuler', '.modal').trigger('click')

    expect(calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
    expect(wrapper.find('.modal').exists()).toBe(false)
  })

  it('affiche l’erreur si la révocation échoue', async () => {
    stubBff([link(LINK_A)], 500)
    const wrapper = await mountView()

    await button(wrapper, 'Révoquer', `[data-lien="${LINK_A}"]`).trigger('click')
    await button(wrapper, 'Révoquer', '.modal').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('Erreur serveur.')
    expect(wrapper.find(`[data-lien="${LINK_A}"]`).exists()).toBe(true)
  })

  it('copie l’adresse publique du lien', async () => {
    stubBff([link(LINK_A)])
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    const wrapper = await mountView()

    await button(wrapper, 'Copier le lien', `[data-lien="${LINK_A}"]`).trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/s/${LINK_A}`)
    expect(wrapper.find('[role="status"]').text()).toContain('copié')
  })
})
