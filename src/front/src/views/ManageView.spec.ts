import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { ManagedItem } from '../api/gestion'
import ManageView from './ManageView.vue'

const TAGS: ManagedItem[] = [
  {
    id: 1,
    name: 'Banque',
    color: '#a6cee3',
    text_color: '#000000',
    is_inbox_tag: false,
    matching_algorithm: 1,
    match: 'bnp',
    is_insensitive: true,
    document_count: 4,
    owner: 1,
    permissions: { view: { users: [], groups: [] }, change: { users: [], groups: [] } },
    user_can_change: true,
  },
  { id: 2, name: 'Impôts', color: '#1f78b4', text_color: '#ffffff', is_inbox_tag: true, matching_algorithm: 6, document_count: 0, user_can_change: false },
]

type Handler = (url: URL, init: RequestInit | undefined) => Response | undefined

/** Faux BFF : listes « Gérer », listes de référence, comptes ; `handler` prend la main en premier. */
function stubBff(handler: Handler = () => undefined, count = TAGS.length) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    const special = handler(url, init)
    if (special) return special
    if (url.pathname.startsWith('/api/gestion/') && (init?.method ?? 'GET') === 'GET') return Response.json({ count, results: TAGS })
    if (url.pathname === '/api/references/utilisateurs') return Response.json([{ id: 2, username: 'bob', first_name: '', last_name: '' }])
    if (url.pathname.startsWith('/api/references/')) return Response.json([])
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const calls = (fetchMock: ReturnType<typeof stubBff>, method = 'GET') =>
  fetchMock.mock.calls
    .filter(([input, init]) => String(input).startsWith('/api/gestion/') && (init?.method ?? 'GET') === method)
    .map(([input, init]) => ({ url: new URL(String(input), 'http://localhost'), body: init?.body ? JSON.parse(String(init.body)) : null }))

async function mountView(path = '/gerer/etiquettes') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/gerer/:type', name: 'gerer', component: ManageView },
      { path: '/documents', name: 'documents', component: { template: '<p>documents</p>' } },
    ],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountView>>['wrapper']

const row = (wrapper: Wrapper, id: number) => wrapper.find(`tr[data-id="${id}"]`)
const button = (wrapper: Wrapper, label: string) => {
  const found = wrapper.findAll('button').find((b) => b.text().startsWith(label))
  expect(found, `bouton « ${label} »`).toBeDefined()
  return found!
}

describe('ManageView', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('liste les étiquettes avec classement, nombre de documents et lien filtré', async () => {
    const fetchMock = stubBff()
    const { wrapper } = await mountView()

    expect(wrapper.find('h1').text()).toBe('Étiquettes')
    expect(calls(fetchMock)[0]!.url.pathname).toBe('/api/gestion/tags')
    expect(calls(fetchMock)[0]!.url.searchParams.get('tri')).toBe('name')
    expect(row(wrapper, 1).text()).toContain('Un des mots : bnp')
    expect(row(wrapper, 1).find('a').attributes('href')).toBe('/documents?tags=1')
    expect(row(wrapper, 2).text()).toContain('Boîte de réception')
    // Sans droit de modification : consultation seule, pas de suppression.
    expect(row(wrapper, 2).text()).toContain('Voir')
    expect(row(wrapper, 2).text()).not.toContain('Supprimer')
  })

  it('trie par nombre de documents puis inverse le tri', async () => {
    const fetchMock = stubBff()
    const { wrapper } = await mountView()

    await button(wrapper, 'Documents').trigger('click')
    await flushPromises()
    expect(calls(fetchMock).at(-1)!.url.searchParams.get('tri')).toBe('document_count')
    await button(wrapper, 'Documents').trigger('click')
    await flushPromises()
    expect(calls(fetchMock).at(-1)!.url.searchParams.get('tri')).toBe('-document_count')
  })

  it('filtre par nom après la saisie', async () => {
    vi.useFakeTimers()
    const fetchMock = stubBff()
    const { wrapper } = await mountView()

    await wrapper.find('input[type="search"]').setValue('ban')
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()
    expect(calls(fetchMock).at(-1)!.url.searchParams.get('nom')).toBe('ban')
  })

  it('pagine au-delà de 25 éléments', async () => {
    const fetchMock = stubBff(undefined, 60)
    const { wrapper } = await mountView()

    expect(wrapper.find('.pagination').text()).toContain('Page 1 sur 3')
    await button(wrapper, 'Suivante').trigger('click')
    await flushPromises()
    expect(calls(fetchMock).at(-1)!.url.searchParams.get('page')).toBe('2')
  })

  it('crée un élément puis recharge la liste et les listes de référence', async () => {
    const fetchMock = stubBff((_url, init) =>
      init?.method === 'POST' ? Response.json({ id: 9, name: 'Assurance' }, { status: 201 }) : undefined,
    )
    const { wrapper } = await mountView('/gerer/correspondants')

    await button(wrapper, '+ Nouveau correspondant').trigger('click')
    await wrapper.find('#gestion-nom').setValue(' Assurance ')
    await wrapper.find('#gestion-algo').setValue('3')
    await wrapper.find('#gestion-match').setValue('MAIF')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const created = calls(fetchMock, 'POST')[0]!
    expect(created.url.pathname).toBe('/api/gestion/correspondants')
    expect(created.body).toEqual({ name: 'Assurance', matching_algorithm: 3, match: 'MAIF', is_insensitive: true })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('« Assurance » est enregistré.')
    expect(fetchMock.mock.calls.some(([input]) => String(input) === '/api/references/correspondants')).toBe(true)
  })

  it('modifie une étiquette sans renvoyer les permissions inchangées', async () => {
    const fetchMock = stubBff((_url, init) =>
      init?.method === 'PATCH' ? Response.json({ ...TAGS[0], name: 'Banques' }) : undefined,
    )
    const { wrapper } = await mountView()

    await row(wrapper, 1).findAll('button')[0]!.trigger('click')
    await flushPromises()
    expect((wrapper.find('#gestion-nom').element as HTMLInputElement).value).toBe('Banque')
    expect(wrapper.find('#gestion-proprietaire').exists()).toBe(true)
    await wrapper.find('#gestion-nom').setValue('Banques')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const patch = calls(fetchMock, 'PATCH')[0]!
    expect(patch.url.pathname).toBe('/api/gestion/tags/1')
    expect(patch.body).toMatchObject({ name: 'Banques', color: '#a6cee3', is_inbox_tag: false, matching_algorithm: 1, match: 'bnp' })
    expect(patch.body).not.toHaveProperty('set_permissions')
  })

  it('envoie les permissions modifiées', async () => {
    const fetchMock = stubBff((_url, init) => (init?.method === 'PATCH' ? Response.json(TAGS[0]) : undefined))
    const { wrapper } = await mountView()

    await row(wrapper, 1).findAll('button')[0]!.trigger('click')
    await flushPromises()
    await wrapper.find('#gestion-proprietaire').setValue('')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(calls(fetchMock, 'PATCH')[0]!.body).toMatchObject({ owner: null, set_permissions: TAGS[0]!.permissions })
  })

  it('affiche le refus de Paperless dans la fenêtre', async () => {
    stubBff((_url, init) =>
      init?.method === 'PATCH'
        ? Response.json({ title: 'Paperless refuse la modification : name : existe déjà', code: 'modification_refusee' }, { status: 400 })
        : undefined,
    )
    const { wrapper } = await mountView()

    await row(wrapper, 1).findAll('button')[0]!.trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="dialog"] .alert-danger').text()).toContain('existe déjà')
  })

  it('supprime après confirmation en annonçant la conséquence', async () => {
    const fetchMock = stubBff((_url, init) => (init?.method === 'DELETE' ? new Response(null, { status: 204 }) : undefined))
    const { wrapper } = await mountView()

    await row(wrapper, 1).findAll('button')[1]!.trigger('click')
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.text()).toContain('Supprimer l’étiquette « Banque » ?')
    expect(dialog.text()).toContain('retiré de 4 documents')
    expect(calls(fetchMock, 'DELETE')).toHaveLength(0)

    await button(wrapper, 'Supprimer définitivement').trigger('click')
    await flushPromises()
    expect(calls(fetchMock, 'DELETE')[0]!.url.pathname).toBe('/api/gestion/tags/1')
    expect(wrapper.text()).toContain('« Banque » est supprimé.')
  })

  it('crée un champ liste de choix', async () => {
    const fetchMock = stubBff((_url, init) =>
      init?.method === 'POST' ? Response.json({ id: 3, name: 'Personne', data_type: 'select' }, { status: 201 }) : undefined,
    )
    const { wrapper } = await mountView('/gerer/champs')

    await button(wrapper, '+ Nouveau champ personnalisé').trigger('click')
    expect(wrapper.find('#gestion-algo').exists()).toBe(false)
    await wrapper.find('#gestion-nom').setValue('Personne')
    await wrapper.find('#gestion-type').setValue('select')
    await button(wrapper, '+ Ajouter un choix').trigger('click')
    await button(wrapper, '+ Ajouter un choix').trigger('click')
    const choices = wrapper.findAll('input[aria-label^="Choix"]')
    await choices[0]!.setValue('Kévin')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(calls(fetchMock, 'POST')[0]!.body).toEqual({
      name: 'Personne',
      data_type: 'select',
      extra_data: { select_options: [{ label: 'Kévin' }] },
    })
  })

  it('signale une liste injoignable et permet de réessayer', async () => {
    let fail = true
    stubBff((url, init) =>
      fail && (init?.method ?? 'GET') === 'GET' && url.pathname.startsWith('/api/gestion/')
        ? Response.json({ title: 'Paperless ne répond pas, réessayez plus tard.' }, { status: 503 })
        : undefined,
    )
    const { wrapper } = await mountView()

    expect(wrapper.find('.alert-danger').text()).toContain('Paperless ne répond pas')
    fail = false
    await button(wrapper, 'Réessayer').trigger('click')
    await flushPromises()
    expect(row(wrapper, 1).exists()).toBe(true)
  })
})
