import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { memoryBackend, offlineDb, setOfflineBackend } from '../offline/db'
import type { SharedFileRecord } from '../offline/shared'
import { useUploadsStore } from '../stores/uploads'
import ShareTargetView from './ShareTargetView.vue'

const PERSONNE = {
  id: 2,
  name: 'Personne',
  data_type: 'select',
  extra_data: { select_options: [{ id: 'opt-k', label: 'Kévin' }] },
}

async function share(key: string, name: string, content: BlobPart = 'x', receivedAt = 1) {
  await offlineDb.put('partages', {
    key,
    name,
    type: 'application/pdf',
    lastModified: 1000,
    blob: new Blob([content]),
    receivedAt,
  } satisfies SharedFileRecord)
}

const keys = async () => (await offlineDb.getAll<SharedFileRecord>('partages')).map((r) => r.key)

beforeEach(() => {
  setActivePinia(createPinia())
  setOfflineBackend(memoryBackend())
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => Response.json(url.endsWith('/champs') ? [PERSONNE] : [])),
  )
})

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  setOfflineBackend(null)
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const mounted: { unmount: () => void }[] = []

async function mountView(path = '/partager') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/partager', name: 'partager', component: ShareTargetView },
      { path: '/documents', name: 'documents', component: { template: '<p>documents</p>' } },
    ],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  mounted.push(wrapper)
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountView>>['wrapper']
const button = (wrapper: Wrapper, label: string) => wrapper.findAll('button').find((b) => b.text().startsWith(label))
const names = (wrapper: Wrapper) => wrapper.findAll('[data-test="fichier"] .nom').map((n) => n.text())

describe('ShareTargetView', () => {
  it('explique comment partager quand rien n’est en attente', async () => {
    const { wrapper } = await mountView()

    expect(wrapper.find('[data-test="vide"]').exists()).toBe(true)
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('envoie les fichiers reçus avec la saisie facultative, puis les retire de l’appareil', async () => {
    await share('a', 'facture.pdf', 'x', 1)
    await share('b', 'devis.pdf', 'y', 2)
    const { wrapper, router } = await mountView('/partager?recus=2')
    const uploads = useUploadsStore()
    const add = vi.spyOn(uploads, 'add').mockImplementation(() => {})

    expect(names(wrapper)).toEqual(['facture.pdf', 'devis.pdf'])
    // Plusieurs fichiers : pas de titre commun.
    expect(wrapper.find('#partage-titre').exists()).toBe(false)
    await wrapper.find('#partage-personne').setValue('opt-k')
    expect(button(wrapper, 'Envoyer 2 fichiers')).toBeDefined()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(add).toHaveBeenCalledTimes(1)
    const [files, metadata] = add.mock.calls[0]!
    expect([...files].map((f) => f.name)).toEqual(['facture.pdf', 'devis.pdf'])
    expect(metadata).toEqual({ title: '', tags: [], customFields: { 2: 'opt-k' } })
    expect(await keys()).toEqual([])
    expect(wrapper.find('[role="status"]').text()).toContain('2 fichiers envoyés')
    expect(router.currentRoute.value.query).toEqual({})
  })

  it('propose un titre pour un fichier seul', async () => {
    await share('a', 'facture.pdf')
    const { wrapper } = await mountView()
    const add = vi.spyOn(useUploadsStore(), 'add').mockImplementation(() => {})

    await wrapper.find('#partage-titre').setValue('Facture EDF')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(add.mock.calls[0]![1]).toMatchObject({ title: 'Facture EDF' })
  })

  it('signale et écarte les fichiers vides ou trop volumineux', async () => {
    await share('vide', 'vide.pdf', '', 1)
    await share('ok', 'facture.pdf', 'x', 2)
    const { wrapper } = await mountView()
    const add = vi.spyOn(useUploadsStore(), 'add').mockImplementation(() => {})

    expect(wrapper.text()).toContain('Fichier vide')
    expect(button(wrapper, 'Envoyer 1 fichier')).toBeDefined()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect([...add.mock.calls[0]![0]].map((f) => f.name)).toEqual(['facture.pdf'])
  })

  it('retire un fichier, ou abandonne tout le partage', async () => {
    await share('a', 'facture.pdf', 'x', 1)
    await share('b', 'devis.pdf', 'y', 2)
    const { wrapper } = await mountView()
    const add = vi.spyOn(useUploadsStore(), 'add')

    await wrapper.find('[aria-label="Retirer facture.pdf"]').trigger('click')
    await flushPromises()
    expect(names(wrapper)).toEqual(['devis.pdf'])
    expect(await keys()).toEqual(['b'])

    await button(wrapper, 'Abandonner')!.trigger('click')
    await flushPromises()
    expect(await keys()).toEqual([])
    expect(wrapper.find('[data-test="vide"]').exists()).toBe(true)
    expect(add).not.toHaveBeenCalled()
  })

  it.each([
    ['service-worker', 'ouvrez Home-GED une fois'],
    ['aucun-fichier', 'Aucun fichier reçu'],
    ['reception', 'n’ont pas pu être gardés'],
    ['inconnu', 'n’ont pas pu être gardés'],
  ])('affiche l’erreur « %s »', async (code, text) => {
    const { wrapper } = await mountView(`/partager?erreur=${code}`)
    expect(wrapper.find('[role="alert"]').text()).toContain(text)
  })
})
