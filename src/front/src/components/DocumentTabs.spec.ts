import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DocumentDetail } from '../api/documents'
import { useAuthStore } from '../stores/auth'
import { useReferencesStore } from '../stores/references'
import DocumentHistory from './DocumentHistory.vue'
import DocumentPermissions from './DocumentPermissions.vue'
import DocumentVersions from './DocumentVersions.vue'

interface Call {
  method: string
  path: string
  body: unknown
}

/** Faux BFF : routes → réponse (ou fonction), appels enregistrés. */
function stubBff(routes: Record<string, Response | ((body: unknown) => Response)>) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      const path = String(input)
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
      calls.push({ method, path, body })
      const route = routes[`${method} ${path}`]
      if (!route) return new Response(null, { status: 404 })
      return typeof route === 'function' ? route(body) : route.clone()
    }),
  )
  return calls
}

const writes = (calls: Call[]) => calls.filter((c) => c.method !== 'GET')

describe('onglet Historique', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('affiche les modifications avec les noms connus', async () => {
    useReferencesStore().lists.correspondants = [{ id: 4, name: 'EDF' }]
    stubBff({
      'GET /api/documents/5/history': Response.json([
        { id: 1, timestamp: '2026-11-02T10:00:00Z', action: 'update', changes: { correspondent: ['None', '4'] }, actor: { id: 1, username: 'alice' } },
      ]),
    })
    const wrapper = mount(DocumentHistory, { props: { documentId: 5 } })
    await flushPromises()

    const entry = wrapper.find('[data-entree="1"]')
    expect(entry.text()).toContain('Modification')
    expect(entry.text()).toContain('par alice')
    expect(entry.find('ins').text()).toBe('EDF')
  })

  it("explique un historique réservé", async () => {
    stubBff({
      'GET /api/documents/5/history': Response.json(
        { title: "L'historique est réservé au propriétaire…", code: 'historique_reserve' },
        { status: 403 },
      ),
    })
    const wrapper = mount(DocumentHistory, { props: { documentId: 5 } })
    await flushPromises()

    expect(wrapper.find('.alert').text()).toContain('réservé au propriétaire')
  })
})

describe('onglet Permissions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })
  afterEach(() => vi.unstubAllGlobals())

  const permissions = (owner: number | null) =>
    Response.json({ owner, permissions: { view: { users: [2], groups: [] }, change: { users: [], groups: [] } } })

  it('le propriétaire ajoute un groupe et retire une personne', async () => {
    const calls = stubBff({
      'GET /api/documents/5/permissions': permissions(1),
      'GET /api/references/utilisateurs': Response.json([
        { id: 1, username: 'alice' },
        { id: 2, username: 'bob', first_name: 'Bob', last_name: 'M.' },
      ]),
      'GET /api/references/groupes': Response.json([{ id: 7, name: 'Foyer' }]),
      'PATCH /api/documents/5/permissions': (body) => {
        const b = body as { owner: number; set_permissions: unknown }
        return Response.json({ owner: b.owner, permissions: b.set_permissions })
      },
    })
    const wrapper = mount(DocumentPermissions, { props: { documentId: 5 } })
    await flushPromises()

    const view = wrapper.find('[data-action="view"]')
    expect(view.text()).toContain('Bob M. (bob)')
    await view.find('select').setValue('groups:7')
    await view.find('button[aria-label="Retirer Bob M. (bob)"]').trigger('click')
    await wrapper.findAll('button').find((b) => b.text() === 'Enregistrer les permissions')!.trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([
      {
        method: 'PATCH',
        path: '/api/documents/5/permissions',
        body: { owner: 1, set_permissions: { view: { users: [], groups: [7] }, change: { users: [], groups: [] } } },
      },
    ])
    expect(wrapper.find('[role="status"]').text()).toBe('Permissions enregistrées.')
    expect(wrapper.find('[data-action="view"]').text()).toContain('Groupe Foyer')
  })

  it("en lecture seule quand on n'est pas propriétaire", async () => {
    stubBff({
      'GET /api/documents/5/permissions': permissions(2),
      'GET /api/references/utilisateurs': Response.json([]),
      'GET /api/references/groupes': Response.json([]),
    })
    const wrapper = mount(DocumentPermissions, { props: { documentId: 5 } })
    await flushPromises()

    expect(wrapper.text()).toContain('Seul le propriétaire du document peut modifier')
    expect(wrapper.find('#fiche-proprietaire').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).not.toContain('Enregistrer les permissions')
  })

  it('sans accès aux listes, affiche les identifiants et le conseil', async () => {
    stubBff({
      'GET /api/documents/5/permissions': permissions(null),
      'GET /api/references/utilisateurs': Response.json({ title: 'Accès refusé par Paperless.' }, { status: 403 }),
      'GET /api/references/groupes': Response.json({ title: 'Accès refusé par Paperless.' }, { status: 403 }),
    })
    const wrapper = mount(DocumentPermissions, { props: { documentId: 5 } })
    await flushPromises()

    expect(wrapper.text()).toContain('Utilisateur n° 2')
    expect(wrapper.text()).toContain('« voir les utilisateurs »')
  })
})

describe('onglet Versions', () => {
  const document = {
    id: 5,
    versions: [
      { id: 8, added: '2026-11-02T10:00:00Z', version_label: 'Signée', checksum: 'abcdef123456', is_root: false },
      { id: 5, added: '2026-11-01T10:00:00Z', version_label: null, checksum: 'fedcba', is_root: true },
    ],
  } as unknown as DocumentDetail

  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
    window.document.body.innerHTML = ''
  })

  const mountVersions = (shown: number | null = null) =>
    mount(DocumentVersions, { props: { document, shown }, attachTo: window.document.body })

  it('liste les versions, affiche et télécharge une version', async () => {
    stubBff({})
    const wrapper = mountVersions()

    const cards = wrapper.findAll('[data-version]')
    expect(cards.map((c) => c.find('strong').text())).toEqual(['Signée', "Version d'origine"])
    expect(cards[0]!.text()).toContain('Actuelle')
    expect(cards[1]!.find('a').attributes('href')).toBe('/api/documents/5/download?version=5')
    expect(cards[1]!.text()).not.toContain('Supprimer')

    await cards[1]!.findAll('button').find((b) => b.text() === 'Afficher')!.trigger('click')
    await cards[0]!.findAll('button').find((b) => b.text() === 'Afficher')!.trigger('click')
    expect(wrapper.emitted('show')).toEqual([[5], [null]])
  })

  it('renomme puis supprime après confirmation', async () => {
    const calls = stubBff({
      'PATCH /api/documents/5/versions/8': Response.json({}),
      'DELETE /api/documents/5/versions/8': new Response(null, { status: 204 }),
    })
    const wrapper = mountVersions(8)
    const card = () => wrapper.find('[data-version="8"]')

    await card().findAll('button').find((b) => b.text() === 'Renommer')!.trigger('click')
    await card().find('input').setValue('  Contresignée ')
    await card().find('form').trigger('submit')
    await flushPromises()

    await card().findAll('button').find((b) => b.text() === 'Supprimer')!.trigger('click')
    expect(wrapper.find('[role="dialog"]').text()).toContain('définitivement')
    await wrapper.find('[role="dialog"] .btn-danger').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([
      { method: 'PATCH', path: '/api/documents/5/versions/8', body: { versionLabel: 'Contresignée' } },
      { method: 'DELETE', path: '/api/documents/5/versions/8', body: undefined },
    ])
    expect(wrapper.emitted('changed')).toHaveLength(2)
    expect(wrapper.emitted('show')).toEqual([[null]])
  })

  it('refuse un fichier trop gros avant tout envoi', async () => {
    stubBff({})
    const wrapper = mountVersions()
    const input = wrapper.find('input[type="file"]')
    const big = new File(['x'], 'gros.pdf')
    Object.defineProperty(big, 'size', { value: 201 * 1024 * 1024 })
    Object.defineProperty(input.element, 'files', { value: [big] })

    await input.trigger('change')

    expect(wrapper.find('[role="alert"]').text()).toContain('200 Mo')
  })

  it("envoie une nouvelle version et suit le traitement jusqu'au bout", async () => {
    vi.useFakeTimers()
    const sent: { url: string; headers: Record<string, string>; form: FormData }[] = []
    class FakeXhr {
      upload = { onprogress: null as ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null }
      onload: (() => void) | null = null
      onerror: (() => void) | null = null
      status = 0
      responseText = ''
      withCredentials = false
      private url = ''
      private headers: Record<string, string> = {}
      open(_method: string, url: string) {
        this.url = url
      }
      setRequestHeader(name: string, value: string) {
        this.headers[name] = value
      }
      send(form: FormData) {
        sent.push({ url: this.url, headers: this.headers, form })
        this.upload.onprogress?.({ lengthComputable: true, loaded: 50, total: 100 })
        this.status = 202
        this.responseText = '{"tache":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}'
        queueMicrotask(() => this.onload?.())
      }
    }
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    let polls = 0
    stubBff({
      'GET /api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3301': () =>
        Response.json({ status: ++polls < 2 ? 'started' : 'success', message: null, document_id: 9 }),
    })
    const wrapper = mountVersions()
    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [new File(['%PDF'], 'v3.pdf', { type: 'application/pdf' })] })
    await input.trigger('change')
    await wrapper.find('input[aria-label="Libellé de la nouvelle version"]').setValue('Signée')

    await wrapper.find('form.card').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[role="status"]').text()).toContain('Paperless traite')
    await vi.advanceTimersByTimeAsync(2000)
    await flushPromises()

    expect(sent).toHaveLength(1)
    expect(sent[0]!.url).toBe('/api/documents/5/versions')
    expect(sent[0]!.headers['X-Requested-With']).toBe('HomeGed')
    expect((sent[0]!.form.get('document') as File).name).toBe('v3.pdf')
    expect(sent[0]!.form.get('version_label')).toBe('Signée')
    expect(polls).toBe(2)
    expect(wrapper.emitted('changed')).toHaveLength(1)
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })

  it("signale l'échec du traitement par Paperless", async () => {
    class FakeXhr {
      upload = { onprogress: null }
      onload: (() => void) | null = null
      status = 202
      responseText = '{"tache":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}'
      withCredentials = false
      open() {}
      setRequestHeader() {}
      send() {
        queueMicrotask(() => this.onload?.())
      }
    }
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    stubBff({
      'GET /api/taches/3f2504e0-4f89-11d3-9a0c-0305e82c3301': Response.json({
        status: 'failure',
        message: 'It is a duplicate of Facture (#5).',
        document_id: null,
      }),
    })
    const wrapper = mountVersions()
    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [new File(['%PDF'], 'v3.pdf')] })
    await input.trigger('change')

    await wrapper.find('form.card').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('duplicate of Facture')
    expect(wrapper.emitted('changed')).toBeUndefined()
  })
})
