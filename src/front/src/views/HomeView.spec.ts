import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import HomeView from './HomeView.vue'

async function mountHome() {
  const page = { template: '<p />' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: HomeView },
      { path: '/documents', name: 'documents', component: page },
      { path: '/documents/:id', name: 'document', component: page },
      { path: '/gerer/:type', name: 'gerer', component: page },
    ],
  })
  await router.push('/')
  const wrapper = mount(HomeView, { global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

const VIEWS = {
  vues: [
    { id: 3, name: 'Impôts', filter_rules: [{ rule_type: 6, value: '2' }], sort_field: 'title', sort_reverse: false, owner: 7 },
    { id: 5, name: 'Pas affichée', filter_rules: [], sort_field: null, sort_reverse: false, owner: 7 },
  ],
  tableau_de_bord: [3],
  menu: [],
}

const STATISTICS = {
  documents_total: 1234,
  documents_inbox: 4,
  inbox_tags: [1],
  tag_count: 12,
  correspondent_count: 30,
  document_type_count: 8,
  storage_path_count: 2,
  character_count: 1,
  document_file_type_counts: [],
}

function stubBff(overrides: Record<string, () => Response> = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost')
    const override = overrides[url.pathname]
    if (override) return override()
    if (url.pathname === '/api/info') return Response.json({ version: '1.2.3' })
    if (url.pathname === '/api/vues') return Response.json(VIEWS)
    if (url.pathname === '/api/tableau-de-bord/statistiques') return Response.json(STATISTICS)
    if (url.pathname === '/api/tableau-de-bord/echeances')
      return Response.json({
        champ: { id: 6, name: 'Échéance' },
        count: 3,
        results: [{ id: 31, title: 'Assurance auto', correspondent: null, tags: [], echeance: '2099-12-01' }],
      })
    if (url.pathname === '/api/references/correspondants') return Response.json([{ id: 4, name: 'EDF' }])
    if (url.pathname.startsWith('/api/references/')) return Response.json([])
    if (url.pathname === '/api/documents') {
      const tagged = url.searchParams.get('tags__id__all') === '2'
      return Response.json({
        count: tagged ? 9 : 40,
        results: [
          {
            id: tagged ? 21 : 11,
            title: tagged ? 'Avis 2026' : 'Facture électricité',
            tags: [],
            correspondent: 4,
            document_type: null,
            storage_path: null,
            created: '2026-03-15',
            added: '2026-11-20T10:00:00+01:00',
            archive_serial_number: null,
          },
        ],
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const widget = (wrapper: Awaited<ReturnType<typeof mountHome>>, title: string) =>
  wrapper.findAll('section.widget').find((section) => section.find('h2').text() === title)!

describe('HomeView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 7, username: 'alice' }
  })
  afterEach(() => vi.unstubAllGlobals())

  it('affiche les vues choisies pour le tableau de bord avec un lien vers la liste', async () => {
    const fetchMock = stubBff()
    const wrapper = await mountHome()

    const impots = widget(wrapper, 'Impôts')
    expect(impots.text()).toContain('Avis 2026')
    expect(impots.text()).toContain('EDF')
    expect(impots.find('a.btn-link').text()).toBe('Tout voir (9)')
    expect(impots.find('a.btn-link').attributes('href')).toBe('/documents?tags=2&tri=title&ordre=asc&vue=3')
    expect(wrapper.text()).not.toContain('Pas affichée')

    const params = fetchMock.mock.calls
      .map(([input]) => new URL(String(input), 'http://localhost'))
      .find((url) => url.searchParams.get('tags__id__all') === '2')!.searchParams
    expect(params.get('page_size')).toBe('5')
    expect(params.get('ordering')).toBe('title')
  })

  it('affiche les derniers ajouts, triés par date d’ajout', async () => {
    const fetchMock = stubBff()
    const wrapper = await mountHome()

    const recent = widget(wrapper, 'Derniers ajouts')
    expect(recent.text()).toContain('Facture électricité')
    expect(recent.find('a.btn-link').attributes('href')).toBe('/documents?tri=added')
    const orderings = fetchMock.mock.calls
      .map(([input]) => new URL(String(input), 'http://localhost'))
      .filter((url) => url.pathname === '/api/documents')
      .map((url) => url.searchParams.get('ordering'))
    expect(orderings).toContain('-added')
  })

  it('affiche les prochaines échéances et le nombre restant', async () => {
    stubBff()
    const wrapper = await mountHome()

    const deadlines = widget(wrapper, 'Échéances à venir')
    expect(deadlines.find('a').attributes('href')).toBe('/documents/31')
    expect(deadlines.text()).toContain('1 déc. 2099')
    expect(deadlines.text()).toContain('Et 2 autres plus tard.')
  })

  it('explique comment activer les échéances sans champ « Échéance »', async () => {
    stubBff({ '/api/tableau-de-bord/echeances': () => Response.json({ champ: null, count: 0, results: [] }) })
    const wrapper = await mountHome()

    expect(widget(wrapper, 'Échéances à venir').text()).toContain('nommé « Échéance »')
  })

  it('affiche les statistiques avec un lien vers la boîte de réception', async () => {
    stubBff()
    const wrapper = await mountHome()

    const statistics = widget(wrapper, 'Statistiques')
    expect(statistics.text()).toContain('documents')
    expect(statistics.text()).toMatch(/1\s234/)
    const inbox = statistics.findAll('a').find((a) => a.text().includes('boîte de réception'))!
    expect(inbox.attributes('href')).toBe('/documents?tags=1')
    expect(statistics.findAll('a').find((a) => a.text() === 'Correspondants')!.attributes('href')).toBe('/gerer/correspondants')
  })

  it('reste utilisable si le BFF ne répond pas, et propose de réessayer', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))

    const wrapper = await mountHome()

    expect(wrapper.text()).toContain('Tableau de bord')
    expect(wrapper.text()).toContain('Ajouter')
    expect(wrapper.text()).not.toContain('version')
    expect(widget(wrapper, 'Statistiques').find('button').text()).toBe('Réessayer')
    expect(widget(wrapper, 'Échéances à venir').find('button').text()).toBe('Réessayer')
  })

  it('affiche la version renvoyée par le BFF', async () => {
    stubBff()
    const wrapper = await mountHome()

    expect(wrapper.text()).toContain('version 1.2.3')
  })
})
