import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import FilterBar from '../components/FilterBar.vue'
import { defaultFilters } from '../filters/documentFilters'
import { useAuthStore } from '../stores/auth'
import { usePreferencesStore } from '../stores/preferences'
import DocumentsView from './DocumentsView.vue'

const TAGS = [{ id: 1, name: 'Banque', color: '#a6cee3', text_color: '#000000', is_inbox_tag: false }]
const CORRESPONDANTS = [{ id: 4, name: 'EDF' }]

function doc(id: number, extra: Record<string, unknown> = {}) {
  return {
    id,
    title: `Document ${id}`,
    tags: [1],
    correspondent: 4,
    document_type: null,
    storage_path: null,
    created: '2026-03-15',
    added: '2026-03-16T10:00:00+01:00',
    archive_serial_number: null,
    content: `Contenu ${id}`,
    ...extra,
  }
}

/** Faux BFF : listes de référence et pages de documents (count documents, 25 par page). */
function stubBff(count: number, options: { fail?: boolean; results?: unknown[] } = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost')
    if (url.pathname === '/api/references/tags') return Response.json(TAGS)
    if (url.pathname === '/api/references/correspondants') return Response.json(CORRESPONDANTS)
    if (url.pathname.startsWith('/api/references/')) return Response.json([])
    if (url.pathname === '/api/documents') {
      if (options.fail) return Response.json({ title: 'Paperless ne répond pas.' }, { status: 503 })
      const page = Number(url.searchParams.get('page'))
      const first = (page - 1) * 25 + 1
      const results =
        options.results ?? Array.from({ length: Math.max(0, Math.min(25, count - first + 1)) }, (_, i) => doc(first + i))
      return Response.json({ count, all: [], results })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const documentCalls = (fetchMock: ReturnType<typeof stubBff>) =>
  fetchMock.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === '/api/documents')

async function mountView(path = '/documents') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/documents', name: 'documents', component: DocumentsView }],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  await flushPromises()
  return { wrapper, router }
}

function stubMobile(mobile: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: mobile && query.includes('max-width'),
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

describe('DocumentsView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    useAuthStore().user = { id: 7, username: 'alice' }
    stubMobile(false)
  })
  afterEach(() => vi.unstubAllGlobals())

  it("charge la page avec les filtres de l'URL et affiche badges et correspondant", async () => {
    const fetchMock = stubBff(2)

    const { wrapper } = await mountView('/documents?q=facture&tags=1&proprio=moi')

    const [call] = documentCalls(fetchMock)
    expect(call!.searchParams.get('title_content')).toBe('facture')
    expect(call!.searchParams.get('tags__id__all')).toBe('1')
    expect(call!.searchParams.get('owner__id')).toBe('7')
    expect(call!.searchParams.get('page_size')).toBe('25')
    expect(wrapper.findAll('[data-document]')).toHaveLength(2)
    expect(wrapper.text()).toContain('2 documents')
    expect(wrapper.text()).toContain('EDF')
    expect(wrapper.find('.badge.etiquette').text()).toBe('Banque')
    expect(wrapper.find('img').attributes('src')).toBe('/api/documents/1/thumb')
  })

  it("un nouveau filtre remplace l'URL et repart de la première page", async () => {
    const fetchMock = stubBff(60)
    const { wrapper, router } = await mountView('/documents?page=2')

    wrapper.findComponent(FilterBar).vm.$emit('update:modelValue', { ...defaultFilters(), correspondants: [4] })
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ correspondant: '4' })
    const last = documentCalls(fetchMock).at(-1)!
    expect(last.searchParams.get('correspondent__id__in')).toBe('4')
    expect(last.searchParams.get('page')).toBe('1')
  })

  it('pagine sur PC', async () => {
    const fetchMock = stubBff(60)
    const { wrapper, router } = await mountView()

    expect(wrapper.findAll('.page-item')).toHaveLength(5) // Précédente, 1, 2, 3, Suivante
    await wrapper.findAll('.page-link').find((b) => b.text() === 'Suivante')!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query.page).toBe('2')
    expect(documentCalls(fetchMock).at(-1)!.searchParams.get('page')).toBe('2')
    expect(wrapper.find('[aria-current="page"]').text()).toBe('2')
  })

  it('ajoute la page suivante à la liste sur mobile', async () => {
    stubMobile(true)
    stubBff(30)
    const { wrapper } = await mountView()
    expect(wrapper.find('.pagination').exists()).toBe(false)
    expect(wrapper.findAll('[data-document]')).toHaveLength(25)

    await wrapper.findAll('button').find((b) => b.text() === 'Afficher plus')!.trigger('click')
    await flushPromises()

    expect(wrapper.findAll('[data-document]')).toHaveLength(30)
    expect(wrapper.find('.suite').exists()).toBe(false)
  })

  it("change d'affichage et le mémorise", async () => {
    stubBff(1)
    const { wrapper } = await mountView()

    await wrapper.findAll('button').find((b) => b.text() === 'Tableau')!.trigger('click')

    expect(wrapper.find('table').exists()).toBe(true)
    expect(usePreferencesStore().display).toBe('tableau')
    expect(wrapper.find('tbody tr').text()).toContain('15 mars 2026')
  })

  it('surligne les correspondances sans interpréter le HTML', async () => {
    stubBff(1, {
      results: [doc(1, { __search_hit__: { score: 1, rank: 0, highlights: 'payé <span class="match">EDF</span><img src=x onerror=alert(1)>' } })],
    })
    const { wrapper } = await mountView('/documents?q=edf&mode=avance')

    expect(wrapper.find('mark').text()).toBe('EDF')
    expect(wrapper.find('.extrait img').exists()).toBe(false)
  })

  it('affiche une erreur et permet de réessayer', async () => {
    const fetchMock = stubBff(0, { fail: true })
    const { wrapper } = await mountView()
    expect(wrapper.find('[role="alert"]').text()).toContain('Paperless ne répond pas.')

    await wrapper.findAll('button').find((b) => b.text() === 'Réessayer')!.trigger('click')
    await flushPromises()

    expect(documentCalls(fetchMock)).toHaveLength(2)
  })

  it('signale une liste vide', async () => {
    stubBff(0)
    const { wrapper } = await mountView('/documents?q=introuvable')
    expect(wrapper.text()).toContain('Aucun document ne correspond à ces filtres.')
  })
})
