import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useDocumentListStore } from '../stores/documentList'
import DocumentView from './DocumentView.vue'

function fakeDoc(id: number, extra: Record<string, unknown> = {}) {
  return {
    id,
    title: `Facture ${id}`,
    content: `Texte OCR ${id}`,
    tags: [1],
    correspondent: 4,
    document_type: null,
    storage_path: null,
    created: '2026-03-15',
    added: '2026-03-16T10:00:00+01:00',
    modified: '2026-03-16T10:00:00+01:00',
    archive_serial_number: null,
    custom_fields: [{ field: 2, value: 'EUR12.50' }],
    notes: [{ id: 1, note: 'À payer <b>vite</b>', created: '2026-03-16T10:00:00Z', user: { id: 1, username: 'alice' } }],
    owner: 1,
    user_can_change: true,
    original_file_name: 'facture.pdf',
    archived_file_name: 'facture.pdf',
    mime_type: 'application/pdf',
    ...extra,
  }
}

interface Call {
  method: string
  path: string
  body: unknown
}

/** Faux BFF : documents 4, 5, 6 (et ceux de overrides), listes de référence, écritures enregistrées. */
function stubBff(overrides: Record<number, Record<string, unknown>> = {}, failures: Record<string, Response> = {}) {
  const calls: Call[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    const method = init?.method ?? 'GET'
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
    calls.push({ method, path: url.pathname + url.search, body })
    const failure = failures[`${method} ${url.pathname}`]
    if (failure) return failure.clone()
    if (url.pathname === '/api/references/tags') {
      if (method === 'POST')
        return Response.json({ id: 8, name: body.name, color: body.color, text_color: '#000000', is_inbox_tag: false }, { status: 201 })
      return Response.json([{ id: 1, name: 'Banque', color: '#a6cee3', text_color: '#000000', is_inbox_tag: false }])
    }
    if (url.pathname === '/api/references/correspondants') return Response.json([{ id: 4, name: 'EDF' }])
    if (url.pathname === '/api/references/champs')
      return Response.json([{ id: 2, name: 'Montant', data_type: 'monetary', extra_data: { default_currency: 'EUR' } }])
    if (url.pathname.startsWith('/api/references/')) return Response.json([])
    const match = /^\/api\/documents\/(\d+)(\/.*)?$/.exec(url.pathname)
    if (match) {
      const id = Number(match[1])
      const doc = { ...fakeDoc(id), ...overrides[id] }
      if (match[2] === '/notes' && method === 'POST')
        return Response.json([...doc.notes, { id: 2, note: body.note, created: '2026-03-17T10:00:00Z', user: { id: 1, username: 'alice' } }])
      if (match[2] === '/metadata') return Response.json({ original_checksum: 'abc', original_size: 2048, has_archive_version: false, original_metadata: [] })
      if (method === 'PATCH') return Response.json({ ...doc, ...body })
      if (method === 'DELETE') return new Response(null, { status: 204 })
      if (id > 99) return Response.json({ title: 'Document introuvable.' }, { status: 404 })
      return Response.json(doc)
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return calls
}

const writes = (calls: Call[]) => calls.filter((c) => c.method !== 'GET')

async function mountView(path = '/documents/5') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/documents', name: 'documents', component: { template: '<p>liste</p>' } },
      { path: '/documents/:id(\\d+)', name: 'document', component: DocumentView },
    ],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountView>>['wrapper']

const button = (wrapper: Wrapper, label: string) => {
  const found = wrapper.findAll('button').find((b) => b.text() === label)
  expect(found, `bouton « ${label} »`).toBeDefined()
  return found!
}

function stubMobile(mobile: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: mobile && query.includes('max-width'),
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

describe('DocumentView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
    stubMobile(false)
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('affiche le document, ses détails et son aperçu', async () => {
    stubBff()
    const { wrapper } = await mountView()

    expect(wrapper.find('h1').text()).toContain('Facture 5')
    expect(wrapper.find('h1').text()).toContain('n° 5')
    expect((wrapper.find('#titre').element as HTMLInputElement).value).toBe('Facture 5')
    expect((wrapper.find('#correspondant').element as HTMLSelectElement).value).toBe('4')
    expect(wrapper.find('#etiquettes').exists()).toBe(true)
    expect(wrapper.text()).toContain('Banque')
    expect(wrapper.find('iframe').attributes('src')).toBe('/api/documents/5/preview')
    expect(button(wrapper, 'Envoyer').attributes('disabled')).toBeDefined()
    expect(button(wrapper, 'Enregistrer').attributes('disabled')).toBeDefined()
  })

  it("sur mobile, propose d'ouvrir l'aperçu au lieu du cadre", async () => {
    stubMobile(true)
    stubBff()
    const { wrapper } = await mountView()

    expect(wrapper.find('iframe').exists()).toBe(false)
    const link = wrapper.find('a[target="_blank"]')
    expect(link.attributes('href')).toBe('/api/documents/5/preview')
    expect(link.attributes('rel')).toBe('noopener')
  })

  it("n'envoie que les champs modifiés et confirme l'enregistrement", async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('#titre').setValue('Facture EDF')
    await wrapper.find('#correspondant').setValue('')
    await button(wrapper, 'Enregistrer').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([
      { method: 'PATCH', path: '/api/documents/5', body: { title: 'Facture EDF', correspondent: null } },
    ])
    expect(wrapper.find('[role="status"]').text()).toBe('Document enregistré.')
    expect(wrapper.find('h1').text()).toContain('Facture EDF')
  })

  it('« Enregistrer et fermer » revient à la liste avec ses filtres', async () => {
    stubBff()
    useDocumentListStore().remember({ q: 'edf' }, [4, 5, 6])
    const { wrapper, router } = await mountView()

    await wrapper.find('#titre').setValue('Autre titre')
    await button(wrapper, 'Enregistrer et fermer').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('documents')
    expect(router.currentRoute.value.query).toEqual({ q: 'edf' })
  })

  it('« Annuler » rétablit les valeurs', async () => {
    stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('#titre').setValue('Brouillon')
    expect(wrapper.text()).toContain('Modifications non enregistrées')
    await button(wrapper, 'Annuler').trigger('click')

    expect((wrapper.find('#titre').element as HTMLInputElement).value).toBe('Facture 5')
    expect(wrapper.text()).not.toContain('Modifications non enregistrées')
  })

  it('refuse un titre vide sans appeler le BFF', async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('#titre').setValue('   ')
    await button(wrapper, 'Enregistrer').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([])
    expect(wrapper.find('[role="alert"]').text()).toBe('Le titre est obligatoire.')
  })

  it('affiche le refus de Paperless', async () => {
    stubBff({}, {
      'PATCH /api/documents/5': Response.json({ title: 'Paperless refuse la modification : title : trop long', code: 'modification_refusee' }, { status: 400 }),
    })
    const { wrapper } = await mountView()

    await wrapper.find('#titre').setValue('Nouveau')
    await button(wrapper, 'Enregistrer').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('title : trop long')
  })

  it('demande confirmation avant de quitter avec des modifications', async () => {
    stubBff()
    const { wrapper, router } = await mountView()
    await wrapper.find('#titre').setValue('Brouillon')

    await router.push('/documents')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('document')
    expect(wrapper.find('[role="dialog"]').text()).toContain('Les modifications de ce document seront perdues.')

    await button(wrapper, 'Rester').trigger('click')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)

    await router.push('/documents')
    await flushPromises()
    await button(wrapper, 'Quitter sans enregistrer').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('documents')
  })

  it('navigue au document suivant de la liste', async () => {
    const calls = stubBff()
    useDocumentListStore().remember({}, [4, 5, 6])
    const { wrapper, router } = await mountView()
    expect(wrapper.text()).toContain('2 / 3')

    await wrapper.find('button[aria-label="Document suivant"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.params.id).toBe('6')
    expect(calls.some((c) => c.path === '/api/documents/6')).toBe(true)
    expect(wrapper.find('h1').text()).toContain('Facture 6')
    expect(wrapper.find('button[aria-label="Document suivant"]').attributes('disabled')).toBeDefined()
  })

  it('supprime après confirmation et retire le document de la liste', async () => {
    const calls = stubBff()
    useDocumentListStore().remember({ tags: '1' }, [4, 5, 6])
    const { wrapper, router } = await mountView()

    await button(wrapper, 'Supprimer').trigger('click')
    expect(wrapper.find('[role="dialog"]').text()).toContain('corbeille')
    expect(writes(calls)).toEqual([])
    await wrapper.find('[role="dialog"] .btn-danger').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([{ method: 'DELETE', path: '/api/documents/5', body: undefined }])
    expect(router.currentRoute.value.query).toEqual({ tags: '1' })
    expect(useDocumentListStore().ids).toEqual([4, 6])
  })

  it('en lecture seule, rien ne se modifie', async () => {
    stubBff({ 5: { user_can_change: false } })
    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('pas le modifier')
    expect(wrapper.find('#titre').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.barre-actions').exists()).toBe(false)
    expect(wrapper.findAll('button').some((b) => b.text() === 'Supprimer')).toBe(false)
  })

  it('signale un document inaccessible', async () => {
    stubBff()
    const { wrapper } = await mountView('/documents/404')
    expect(wrapper.find('[role="alert"]').text()).toContain('n’existe pas ou ne vous est pas accessible')
  })

  it('onglets Contenu, Métadonnées et Notes (texte brut)', async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('#onglet-contenu').trigger('click')
    expect(wrapper.find('pre').text()).toBe('Texte OCR 5')

    await wrapper.find('#onglet-metadonnees').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('2 Ko')

    await wrapper.find('#onglet-notes').trigger('click')
    expect(wrapper.find('[data-note="1"]').text()).toContain('À payer <b>vite</b>')
    expect(wrapper.find('[data-note="1"] b').exists()).toBe(false)
    await wrapper.find('#nouvelle-note').setValue('Payée')
    await wrapper.find('#nouvelle-note').element.closest('form')!.requestSubmit()
    await flushPromises()
    expect(writes(calls)).toEqual([{ method: 'POST', path: '/api/documents/5/notes', body: { note: 'Payée' } }])
    expect(wrapper.findAll('[data-note]')).toHaveLength(2)
  })

  it('crée une étiquette à la volée et la sélectionne', async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('button[aria-label="Créer une étiquette"]').trigger('click')
    await wrapper.find('input[aria-label="Nom de la nouvelle étiquette"]').setValue('Énergie')
    await wrapper.find('input[aria-label="Nom de la nouvelle étiquette"]').element.closest('form')!.requestSubmit()
    await flushPromises()

    const created = writes(calls)[0]!
    expect(created.path).toBe('/api/references/tags')
    expect(created.body).toMatchObject({ name: 'Énergie' })
    expect(wrapper.find('[aria-label="Étiquettes du document"]').text()).toContain('Énergie')

    await button(wrapper, 'Enregistrer').trigger('click')
    await flushPromises()
    expect(writes(calls).at(-1)!.body).toEqual({ tags: [1, 8] })
  })

  it('saisit un montant dans un champ personnalisé', async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('#champ-2').setValue('99.9')
    await wrapper.find('#champ-2').trigger('change')
    await button(wrapper, 'Enregistrer').trigger('click')
    await flushPromises()

    expect(writes(calls)[0]!.body).toEqual({ custom_fields: [{ field: 2, value: 'EUR99.90' }] })
  })

  it("Entrée enregistre depuis un champ, mais pas depuis un formulaire de création", async () => {
    const calls = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('button[aria-label="Créer une étiquette"]').trigger('click')
    await wrapper.find('input[aria-label="Nom de la nouvelle étiquette"]').trigger('keydown', { key: 'Enter' })
    expect(writes(calls)).toEqual([])

    await wrapper.find('#titre').setValue('Par Entrée')
    await wrapper.find('#titre').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    expect(writes(calls)).toEqual([{ method: 'PATCH', path: '/api/documents/5', body: { title: 'Par Entrée' } }])
  })
})
