import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import FilterBar from '../components/FilterBar.vue'
import { defaultFilters } from '../filters/documentFilters'
import { useAuthStore } from '../stores/auth'
import { useDocumentListStore } from '../stores/documentList'
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
    if (url.pathname === '/api/documents/ids') return Response.json([])
    if (url.pathname === '/api/documents') {
      if (options.fail) return Response.json({ title: 'Paperless ne répond pas.' }, { status: 503 })
      const page = Number(url.searchParams.get('page'))
      const first = (page - 1) * 25 + 1
      const results =
        options.results ?? Array.from({ length: Math.max(0, Math.min(25, count - first + 1)) }, (_, i) => doc(first + i))
      return Response.json({ count, results })
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
    routes: [
      { path: '/documents', name: 'documents', component: DocumentsView },
      { path: '/documents/:id', name: 'document', component: { template: '<p>fiche</p>' } },
    ],
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
    expect(wrapper.find('[data-document="1"] a').attributes('href')).toBe('/documents/1')
  })

  it('mémorise la liste pour précédent / suivant de la fiche', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.startsWith('/api/documents/ids')) return Response.json([7, 3, 9])
      if (url.startsWith('/api/documents')) return Response.json({ count: 3, results: [doc(7), doc(3), doc(9)] })
      return Response.json([])
    })
    vi.stubGlobal('fetch', fetchMock)

    await mountView('/documents?q=edf&page=2')

    const list = useDocumentListStore()
    expect(list.query).toEqual({ q: 'edf', page: '2' })
    const idsCall = fetchMock.mock.calls.map(([input]) => String(input)).find((u) => u.startsWith('/api/documents/ids'))!
    const idsParams = new URL(idsCall, 'http://localhost').searchParams
    expect(idsParams.get('title_content')).toBe('edf')
    expect(idsParams.get('ordering')).toBe('-created')
    expect(idsParams.has('page')).toBe(false)
    expect(list.neighbours(3)).toEqual({ previous: 7, next: 9, position: { index: 2, total: 3 } })
    expect(list.neighbours(7).previous).toBeNull()
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

  describe('sélection multiple et actions en masse (UI-06)', () => {
    /** Faux BFF complet : identifiants de tous les résultats et routes des actions en masse. */
    function stubActions(
      count: number,
      actions: { status?: number; body?: unknown; task?: { status: string; message: string | null } } = {},
    ) {
      const base = stubBff(count).getMockImplementation()!
      const fetchMock = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>(async (input) => {
        const url = new URL(String(input), 'http://localhost')
        if (url.pathname === '/api/documents/ids') return Response.json(Array.from({ length: count }, (_, i) => i + 1))
        if (url.pathname === '/api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3309') {
          return Response.json({ ...actions.task, document_id: null })
        }
        if (url.pathname === '/api/documents/actions' && actions.task) {
          return Response.json({ tache: '3f2504e0-4f89-11d3-9a0c-0305e82c3309' }, { status: 202 })
        }
        if (url.pathname === '/api/documents/actions') {
          return actions.status
            ? Response.json(actions.body ?? {}, { status: actions.status })
            : new Response(null, { status: 204 })
        }
        if (url.pathname === '/api/documents/telechargement') {
          return new Response('PK', {
            headers: { 'Content-Type': 'application/zip', 'Content-Disposition': 'attachment; filename="documents.zip"' },
          })
        }
        return base(input)
      })
      vi.stubGlobal('fetch', fetchMock)
      return fetchMock
    }

    const posts = (fetchMock: ReturnType<typeof stubActions>, path: string) =>
      fetchMock.mock.calls
        .filter(([input, init]) => String(input) === path && init?.method === 'POST')
        .map(([, init]) => JSON.parse(String(init!.body)))

    async function openAction(wrapper: Awaited<ReturnType<typeof mountView>>['wrapper'], action: string) {
      await wrapper.find('[aria-label="Actions sur la sélection"]').trigger('click')
      await wrapper.find(`[data-action="${action}"]`).trigger('click')
      await flushPromises()
    }

    const confirm = (wrapper: Awaited<ReturnType<typeof mountView>>['wrapper'], label: string) =>
      wrapper.findAll('.modal-footer button').find((b) => b.text() === label)!

    it("cocher un document affiche la barre, qui disparaît quand la sélection est vide", async () => {
      stubActions(3)
      const { wrapper } = await mountView()
      expect(wrapper.find('[aria-label="Sélection"]').exists()).toBe(false)

      await wrapper.find('[data-document="2"] input[type="checkbox"]').setValue(true)
      expect(wrapper.find('[aria-label="Sélection"]').text()).toContain('1 sélectionné sur 3')
      expect(wrapper.find('[data-document="2"]').classes()).toContain('choisi')

      await wrapper.find('[data-document="2"] input[type="checkbox"]').setValue(false)
      expect(wrapper.find('[aria-label="Sélection"]').exists()).toBe(false)
    })

    it('sélectionne tous les résultats, puis ajoute une étiquette et recharge la liste', async () => {
      const fetchMock = stubActions(30)
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="1"] input[type="checkbox"]').setValue(true)

      await wrapper.find('[aria-label="Sélectionner"]').trigger('click')
      await wrapper.findAll('.dropdown-item').find((b) => b.text().startsWith('Tous les résultats'))!.trigger('click')
      await flushPromises()
      expect(wrapper.find('[aria-label="Sélection"]').text()).toContain('30 sélectionnés')

      await openAction(wrapper, 'tags')
      expect(confirm(wrapper, 'Appliquer').attributes('disabled')).toBeDefined()
      await wrapper.find('#masse-ajout-etiquettes').setValue('1')
      const loads = documentCalls(fetchMock).length
      await confirm(wrapper, 'Appliquer').trigger('click')
      await flushPromises()

      const [body] = posts(fetchMock, '/api/documents/actions')
      expect(body.documents).toHaveLength(30)
      expect(body).toMatchObject({ method: 'modify_tags', parameters: { add_tags: [1], remove_tags: [] } })
      expect(wrapper.find('.modal').exists()).toBe(false)
      expect(wrapper.find('.alert-success').text()).toContain('30 documents modifiés.')
      expect(wrapper.find('[aria-label="Sélection"]').exists()).toBe(false)
      expect(documentCalls(fetchMock).length).toBe(loads + 1)
    })

    it('supprime après confirmation explicite', async () => {
      const fetchMock = stubActions(3)
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="1"] input[type="checkbox"]').setValue(true)
      await wrapper.find('[data-document="3"] input[type="checkbox"]').setValue(true)

      await openAction(wrapper, 'supprimer')
      expect(wrapper.find('.modal').text()).toContain('2 documents seront placés dans la corbeille')
      expect(posts(fetchMock, '/api/documents/actions')).toHaveLength(0)
      await confirm(wrapper, 'Supprimer').trigger('click')
      await flushPromises()

      expect(posts(fetchMock, '/api/documents/actions')).toEqual([{ documents: [1, 3], method: 'delete', parameters: {} }])
      expect(wrapper.find('.alert-success').text()).toContain('2 documents placés dans la corbeille.')
    })

    it("attend la fin de la tâche de fond de Paperless avant de recharger", async () => {
      const fetchMock = stubActions(3, { task: { status: 'success', message: null } })
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="1"] input[type="checkbox"]').setValue(true)
      const loads = documentCalls(fetchMock).length

      await openAction(wrapper, 'supprimer')
      await confirm(wrapper, 'Supprimer').trigger('click')
      await flushPromises()

      const urls = fetchMock.mock.calls.map(([input]) => String(input))
      const taskIndex = urls.indexOf('/api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3309')
      expect(taskIndex).toBeGreaterThan(-1)
      expect(urls.slice(taskIndex).some((u) => u.startsWith('/api/documents?'))).toBe(true)
      expect(documentCalls(fetchMock).length).toBe(loads + 1)
      expect(wrapper.find('.alert-success').text()).toContain('1 document placé dans la corbeille.')
    })

    it("affiche l'échec de la tâche de fond et garde la sélection", async () => {
      stubActions(3, { task: { status: 'failure', message: 'Insufficient permissions' } })
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="1"] input[type="checkbox"]').setValue(true)

      await openAction(wrapper, 'supprimer')
      await confirm(wrapper, 'Supprimer').trigger('click')
      await flushPromises()

      expect(wrapper.find('.modal [role="alert"]').text()).toContain('Paperless a échoué : Insufficient permissions')
      expect(wrapper.find('[aria-label="Sélection"]').text()).toContain('1 sélectionné')
    })

    it('affiche le refus de Paperless dans la fenêtre et garde la sélection', async () => {
      stubActions(3, { status: 403, body: { title: "Vous n'avez pas le droit de faire cette action.", code: 'acces_refuse' } })
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="1"] input[type="checkbox"]').setValue(true)

      await openAction(wrapper, 'correspondants')
      await wrapper.find('#masse-correspondants').setValue('4')
      await confirm(wrapper, 'Appliquer').trigger('click')
      await flushPromises()

      expect(wrapper.find('.modal [role="alert"]').text()).toContain("Vous n'avez pas le droit")
      expect(wrapper.find('[aria-label="Sélection"]').text()).toContain('1 sélectionné')
    })

    it("télécharge l'archive de la sélection sans la vider", async () => {
      const fetchMock = stubActions(3)
      const createObjectURL = vi.fn(() => 'blob:archive')
      const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL }
      URL.createObjectURL = createObjectURL
      URL.revokeObjectURL = vi.fn()
      onTestFinished(() => {
        URL.createObjectURL = original.create
        URL.revokeObjectURL = original.revoke
      })
      const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
      const { wrapper } = await mountView()
      await wrapper.find('[data-document="2"] input[type="checkbox"]').setValue(true)

      await openAction(wrapper, 'telecharger')
      await wrapper.find('#masse-originaux').setValue(true)
      await confirm(wrapper, 'Télécharger').trigger('click')
      await flushPromises()

      expect(posts(fetchMock, '/api/documents/telechargement')).toEqual([{ documents: [2], contenu: 'originaux' }])
      expect(createObjectURL).toHaveBeenCalled()
      expect(click).toHaveBeenCalled()
      expect(wrapper.find('[aria-label="Sélection"]').exists()).toBe(true)
      click.mockRestore()
    })

    it('la case de l’en-tête du tableau coche la page, un nouveau filtre vide la sélection', async () => {
      stubActions(3)
      usePreferencesStore().display = 'tableau'
      const { wrapper } = await mountView()

      await wrapper.find('thead input[type="checkbox"]').setValue(true)
      expect(wrapper.find('[aria-label="Sélection"]').text()).toContain('3 sélectionnés')

      wrapper.findComponent(FilterBar).vm.$emit('update:modelValue', { ...defaultFilters(), correspondants: [4] })
      await flushPromises()
      expect(wrapper.find('[aria-label="Sélection"]').exists()).toBe(false)
    })
  })

  describe('vues sauvegardées', () => {
    const VIEW = {
      id: 12,
      name: 'Factures EDF',
      filter_rules: [
        { rule_type: 26, value: '4' },
        { rule_type: 21, value: '99' },
      ],
      sort_field: 'created',
      sort_reverse: true,
      owner: 7,
      user_can_change: true,
    }

    function stubViews() {
      const sent: { method: string; path: string; body: unknown }[] = []
      const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input), 'http://localhost')
        const method = init?.method ?? 'GET'
        if (method !== 'GET') {
          sent.push({ method, path: url.pathname, body: init?.body ? JSON.parse(String(init.body)) : null })
          if (method === 'DELETE') return new Response(null, { status: 204 })
          return Response.json({ ...VIEW, id: method === 'POST' ? 13 : 12, name: 'Nouvelle' }, { status: method === 'POST' ? 201 : 200 })
        }
        if (url.pathname === '/api/vues') return Response.json({ vues: [VIEW], tableau_de_bord: [], menu: [12] })
        if (url.pathname.startsWith('/api/references/')) return Response.json([])
        if (url.pathname === '/api/documents/ids') return Response.json([])
        if (url.pathname === '/api/documents') return Response.json({ count: 1, results: [doc(1)] })
        return new Response(null, { status: 404 })
      })
      vi.stubGlobal('fetch', fetchMock)
      return sent
    }

    const menuItem = (wrapper: Awaited<ReturnType<typeof mountView>>['wrapper'], label: string) =>
      wrapper.findAll('.dropdown-item').find((item) => item.text().startsWith(label))!

    async function openMenu(wrapper: Awaited<ReturnType<typeof mountView>>['wrapper']) {
      await wrapper.find('button[aria-label="Vues sauvegardées"]').trigger('click')
    }

    it('affiche le nom de la vue ouverte et signale ses critères non pris en charge', async () => {
      stubViews()
      const { wrapper } = await mountView('/documents?correspondant=4&vue=12')

      expect(wrapper.find('h1').text()).toBe('Factures EDF')
      expect(wrapper.find('.alert-warning').text()).toContain('1 critère')
    })

    it('marque la vue modifiée et la met à jour en conservant les critères inconnus', async () => {
      const sent = stubViews()
      const { wrapper, router } = await mountView('/documents?correspondant=4&vue=12')

      wrapper.findComponent(FilterBar).vm.$emit('update:modelValue', { ...defaultFilters(), correspondants: [4], tags: [1] })
      await flushPromises()
      expect(router.currentRoute.value.query.vue).toBe('12')
      expect(wrapper.find('h1').text()).toContain('modifiée')

      await openMenu(wrapper)
      await menuItem(wrapper, 'Mettre à jour').trigger('click')
      await flushPromises()

      expect(sent).toEqual([
        {
          method: 'PATCH',
          path: '/api/vues/12',
          body: {
            filter_rules: [
              { rule_type: 6, value: '1' },
              { rule_type: 26, value: '4' },
              { rule_type: 21, value: '99' },
            ],
            sort_field: 'created',
            sort_reverse: true,
          },
        },
      ])
      expect(wrapper.find('.alert-success').text()).toContain('mise à jour')
    })

    it('enregistre les filtres comme nouvelle vue et l’ouvre', async () => {
      const sent = stubViews()
      const { wrapper, router } = await mountView('/documents?tags=1&tri=title&ordre=asc')

      await openMenu(wrapper)
      await menuItem(wrapper, 'Enregistrer comme nouvelle vue').trigger('click')
      await wrapper.find('#vue-nom').setValue(' Banque ')
      await wrapper.find('#vue-tableau').setValue(true)
      await wrapper.find('.modal form').trigger('submit')
      await flushPromises()

      expect(sent).toEqual([
        {
          method: 'POST',
          path: '/api/vues',
          body: {
            name: 'Banque',
            filter_rules: [{ rule_type: 6, value: '1' }],
            sort_field: 'title',
            sort_reverse: false,
            tableau_de_bord: true,
            menu: true,
          },
        },
      ])
      expect(router.currentRoute.value.query).toEqual({ tags: '1', tri: 'title', ordre: 'asc', vue: '13' })
    })

    it('refuse une vue sans nom', async () => {
      const sent = stubViews()
      const { wrapper } = await mountView()

      await openMenu(wrapper)
      await menuItem(wrapper, 'Enregistrer comme nouvelle vue').trigger('click')
      await wrapper.find('.modal form').trigger('submit')
      await flushPromises()

      expect(sent).toEqual([])
      expect(wrapper.find('.modal .alert-danger').text()).toContain('nom')
    })

    it('supprime la vue après confirmation et garde les filtres', async () => {
      const sent = stubViews()
      const { wrapper, router } = await mountView('/documents?correspondant=4&vue=12')

      await openMenu(wrapper)
      await menuItem(wrapper, 'Supprimer la vue').trigger('click')
      expect(sent).toEqual([])
      await wrapper.findAll('.modal-footer button').find((b) => b.text() === 'Supprimer')!.trigger('click')
      await flushPromises()

      expect(sent).toEqual([{ method: 'DELETE', path: '/api/vues/12', body: null }])
      expect(router.currentRoute.value.query).toEqual({ correspondant: '4' })
    })

    it('ne propose ni mise à jour ni suppression d’une vue partagée en lecture seule', async () => {
      stubViews()
      VIEW.user_can_change = false
      onTestFinished(() => {
        VIEW.user_can_change = true
      })
      const { wrapper } = await mountView('/documents?correspondant=4&vue=12')

      await openMenu(wrapper)
      const labels = wrapper.findAll('.dropdown-item').map((item) => item.text())
      expect(labels).toContain('Modifier la vue…')
      expect(labels.some((label) => label.startsWith('Mettre à jour'))).toBe(false)
      expect(labels.some((label) => label.startsWith('Supprimer'))).toBe(false)
    })
  })
})
