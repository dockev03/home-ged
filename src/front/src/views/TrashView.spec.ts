import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { daysLeft } from '../api/trash'
import { useOpenDocumentsStore } from '../stores/openDocuments'
import TrashView from './TrashView.vue'

interface Call {
  method: string
  path: string
  body: unknown
}

const NOW = new Date('2026-11-22T12:00:00Z')

/** Faux BFF : une corbeille de `count` documents (2 affichés) ; écritures enregistrées. */
function stubBff(count = 2, failure?: Response) {
  const calls: Call[] = []
  let remaining = count
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const method = init?.method ?? 'GET'
      calls.push({ method, path: url.pathname + url.search, body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined })
      if (url.pathname === '/api/references/tags')
        return Response.json([{ id: 1, name: 'Banque', color: '#a6cee3', text_color: '#000000', is_inbox_tag: false }])
      if (url.pathname === '/api/references/correspondants') return Response.json([{ id: 3, name: 'EDF' }])
      if (url.pathname.startsWith('/api/references/')) return Response.json([])
      if (url.pathname === '/api/corbeille') {
        const results = [
          { id: 12, title: 'Facture EDF', correspondent: 3, document_type: null, tags: [1], created: '2026-10-01', deleted_at: '2026-11-20T12:00:00Z' },
          { id: 14, title: 'Devis', correspondent: null, document_type: null, tags: [], created: '2026-09-12', deleted_at: '2026-10-24T12:00:00Z' },
        ].slice(0, remaining)
        return Response.json({ count: remaining, delai: 30, results })
      }
      if (failure) return failure.clone()
      remaining = 0
      return new Response(null, { status: 204 })
    }),
  )
  return calls
}

const writes = (calls: Call[]) => calls.filter((c) => c.method !== 'GET')

async function mountView() {
  const wrapper = mount(TrashView, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

type Wrapper = Awaited<ReturnType<typeof mountView>>

const button = (wrapper: Wrapper, label: string) => {
  const found = wrapper.findAll('button').find((b) => b.text() === label)
  expect(found, `bouton « ${label} »`).toBeDefined()
  return found!
}

describe('TrashView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('liste les documents avec leur date d’effacement automatique', async () => {
    stubBff()
    const wrapper = await mountView()

    const items = wrapper.findAll('.element')
    expect(items).toHaveLength(2)
    expect(items[0]!.text()).toContain('Facture EDF')
    expect(items[0]!.text()).toContain('EDF')
    expect(items[0]!.text()).toContain('Banque')
    expect(items[0]!.text()).toContain('Effacé dans 28 jours')
    expect(items[1]!.text()).toContain('Effacé dans 1 jour')
    expect(wrapper.text()).toContain('30 jours')
  })

  it('restaure un document', async () => {
    const calls = stubBff()
    const wrapper = await mountView()

    await wrapper.findAll('.element')[1]!.find('button').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([{ method: 'POST', path: '/api/corbeille/restauration', body: { documents: [14] } }])
    expect(wrapper.find('.alert-success').text()).toContain('1 document restauré.')
  })

  it('supprime définitivement la sélection après confirmation', async () => {
    const calls = stubBff()
    const open = useOpenDocumentsStore()
    open.load(1)
    open.open({ id: 12, title: 'Facture EDF' })
    const wrapper = await mountView()

    await wrapper.find('#tout').setValue(true)
    expect(wrapper.text()).toContain('2 documents sélectionnés')
    await button(wrapper, 'Supprimer définitivement').trigger('click')
    expect(wrapper.find('[role="dialog"]').text()).toContain('définitive')
    expect(writes(calls)).toEqual([])
    await wrapper.find('[role="dialog"] .btn-danger').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([{ method: 'POST', path: '/api/corbeille/suppression', body: { documents: [12, 14] } }])
    expect(wrapper.text()).toContain('La corbeille est vide.')
    expect(open.documents).toEqual([])
  })

  it('vide la corbeille après confirmation', async () => {
    const calls = stubBff(2)
    const wrapper = await mountView()

    await button(wrapper, 'Vider la corbeille').trigger('click')
    expect(wrapper.find('[role="dialog"]').text()).toContain('Les 2 documents de la corbeille seront effacés')
    await wrapper.find('[role="dialog"] .btn-secondary, [role="dialog"] .btn-outline-secondary').trigger('click')
    expect(writes(calls)).toEqual([])

    await button(wrapper, 'Vider la corbeille').trigger('click')
    await wrapper.find('[role="dialog"] .btn-danger').trigger('click')
    await flushPromises()

    expect(writes(calls)).toEqual([{ method: 'POST', path: '/api/corbeille/suppression', body: { tout: true } }])
    expect(wrapper.find('.alert-success').text()).toContain('Corbeille vidée.')
  })

  it('affiche le refus de Paperless', async () => {
    stubBff(2, Response.json({ title: 'Vous n’avez pas le droit.', code: 'acces_refuse' }, { status: 403 }))
    const wrapper = await mountView()

    await button(wrapper, 'Vider la corbeille').trigger('click')
    await wrapper.find('[role="dialog"] .btn-danger').trigger('click')
    await flushPromises()

    expect(wrapper.find('.alert-danger').text()).toContain('Vous n’avez pas le droit.')
    expect(wrapper.findAll('.element')).toHaveLength(2)
  })

  it('corbeille vide : rien à vider', async () => {
    stubBff(0)
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('La corbeille est vide.')
    expect(button(wrapper, 'Vider la corbeille').attributes('disabled')).toBeDefined()
  })
})

describe('daysLeft', () => {
  it('compte les jours restants, jamais négatifs, inconnus sans délai', () => {
    expect(daysLeft('2026-11-20T12:00:00Z', 30, NOW)).toBe(28)
    expect(daysLeft('2026-01-01T00:00:00Z', 30, NOW)).toBe(0)
    expect(daysLeft('2026-11-20T12:00:00Z', null, NOW)).toBeNull()
    expect(daysLeft('pas une date', 30, NOW)).toBeNull()
  })
})
