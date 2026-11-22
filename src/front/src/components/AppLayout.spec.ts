import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useOpenDocumentsStore } from '../stores/openDocuments'
import { useUploadsStore } from '../stores/uploads'
import AppLayout from './AppLayout.vue'

async function mountLayout(path = '/') {
  const page = { template: '<p>page</p>' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        component: AppLayout,
        children: [
          { path: '', name: 'accueil', component: page },
          { path: 'documents', name: 'documents', component: page },
          { path: 'sessions', name: 'sessions', component: page },
          { path: 'documents/:id', name: 'document', component: page },
          { path: 'gerer/:type', name: 'gerer', component: page },
          { path: 'corbeille', name: 'corbeille', component: page },
        ],
      },
    ],
  })
  await router.push(path)
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  mounted.push(wrapper)
  await flushPromises()
  return { wrapper, router }
}

/** Démontés après chaque test : leurs écouteurs de fenêtre (beforeunload) ne fuient pas. */
const mounted: { unmount: () => void }[] = []

describe('AppLayout', () => {
  afterEach(() => mounted.splice(0).forEach((wrapper) => wrapper.unmount()))
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().user = { id: 1, username: 'alice' }
  })

  it('affiche le menu, la page et les entrées à venir désactivées', async () => {
    const { wrapper } = await mountLayout('/documents')

    const active = wrapper.find('#menu-principal .nav-link.active')
    expect(active.text()).toBe('Documents')
    expect(wrapper.find('main').text()).toBe('page')
    const scanner = wrapper.findAll('.nav-link.disabled').find((l) => l.text().startsWith('Scanner'))!
    expect(scanner.attributes('aria-disabled')).toBe('true')
    expect(scanner.text()).toContain('bientôt')
  })

  it('mène à la corbeille', async () => {
    const { wrapper } = await mountLayout('/corbeille')

    expect(wrapper.find('#menu-principal .nav-link.active').text()).toBe('Corbeille')
  })

  it('liste les documents ouverts, les ferme un par un ou tous', async () => {
    sessionStorage.clear()
    const open = useOpenDocumentsStore()
    open.load(1)
    open.open({ id: 4, title: 'Devis' })
    open.open({ id: 5, title: 'Facture' })
    const { wrapper, router } = await mountLayout('/documents/5')

    const section = wrapper.find('.documents-ouverts')
    expect(section.findAll('a').map((a) => a.text())).toEqual(['Facture', 'Devis'])
    expect(section.find('a.active').text()).toBe('Facture')

    await section.find('button[aria-label="Fermer Devis"]').trigger('click')
    expect(open.documents.map((d) => d.id)).toEqual([5])
    expect(router.currentRoute.value.name).toBe('document')

    await wrapper.findAll('.documents-ouverts button').find((b) => b.text() === 'Tout fermer')!.trigger('click')
    await flushPromises()
    expect(open.documents).toEqual([])
    expect(router.currentRoute.value.name).toBe('documents')
    expect(wrapper.find('.documents-ouverts').exists()).toBe(false)
  })

  it('mène aux écrans « Gérer » et marque l’écran ouvert', async () => {
    const { wrapper } = await mountLayout('/gerer/correspondants')

    const links = wrapper.findAll('#menu-principal a.nav-link')
    const labels = links.map((l) => l.text())
    expect(labels).toEqual(expect.arrayContaining(['Étiquettes', 'Correspondants', 'Types de documents', 'Chemins de stockage', 'Champs personnalisés']))
    expect(links.find((l) => l.text() === 'Étiquettes')!.attributes('href')).toBe('/gerer/etiquettes')
    const active = wrapper.findAll('#menu-principal .nav-link.active')
    expect(active.map((l) => l.text())).toEqual(['Correspondants'])
  })

  it('affiche les vues choisies pour le menu et marque la vue ouverte', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) =>
        String(input) === '/api/vues'
          ? Response.json({
              vues: [
                { id: 3, name: 'Impôts', filter_rules: [{ rule_type: 6, value: '2' }], sort_field: null, sort_reverse: false, owner: 1 },
                { id: 4, name: 'Cachée', filter_rules: [], sort_field: null, sort_reverse: false, owner: 1 },
              ],
              tableau_de_bord: [],
              menu: [3],
            })
          : new Response(null, { status: 404 }),
      ),
    )
    onTestFinished(() => {
      vi.unstubAllGlobals()
    })
    const { wrapper } = await mountLayout('/documents?tags=2&vue=3')

    const view = wrapper.findAll('#menu-principal a.nav-link').find((l) => l.text() === 'Impôts')!
    expect(view.attributes('href')).toBe('/documents?tags=2&vue=3')
    expect(wrapper.findAll('#menu-principal .nav-link.active').map((l) => l.text())).toEqual(['Impôts'])
    expect(wrapper.text()).not.toContain('Cachée')
  })

  it('lance une recherche plein texte vers la liste des documents', async () => {
    const { wrapper, router } = await mountLayout()

    await wrapper.find('form[role="search"] input').setValue(' facture edf ')
    await wrapper.find('form[role="search"]').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('documents')
    expect(router.currentRoute.value.query).toEqual({ q: 'facture edf', mode: 'avance', tri: 'pertinence' })
  })

  it('reprend la recherche en cours dans la barre', async () => {
    const { wrapper } = await mountLayout('/documents?q=edf')
    expect((wrapper.find('form[role="search"] input').element as HTMLInputElement).value).toBe('edf')
  })

  it('ouvre le tiroir sur mobile et le referme en changeant de page', async () => {
    const { wrapper, router } = await mountLayout()
    const drawer = () => wrapper.find('#menu-principal')

    await wrapper.find('button[aria-label="Ouvrir le menu"]').trigger('click')
    expect(drawer().classes()).toContain('show')
    expect(wrapper.find('.offcanvas-backdrop').exists()).toBe(true)

    await router.push('/documents')
    await flushPromises()
    expect(drawer().classes()).not.toContain('show')

    await wrapper.find('button[aria-label="Ouvrir le menu"]').trigger('click')
    await drawer().trigger('keydown', { key: 'Escape' })
    expect(drawer().classes()).not.toContain('show')
  })

  it('un fichier glissé-déposé est confié au suivi des envois', async () => {
    const { wrapper } = await mountLayout()
    const uploads = useUploadsStore()
    const add = vi.spyOn(uploads, 'add').mockImplementation(() => {})
    const file = new File(['%PDF'], 'facture.pdf', { type: 'application/pdf' })
    const dataTransfer = { types: ['Files'], files: [file], dropEffect: 'none' }

    await wrapper.find('.coque').trigger('dragenter', { dataTransfer })
    expect(wrapper.find('.depot').text()).toContain('Déposez les fichiers')
    await wrapper.find('.coque').trigger('drop', { dataTransfer })

    expect(add).toHaveBeenCalledWith([file])
    expect(wrapper.find('.depot').exists()).toBe(false)
  })

  it('un glisser sans fichier (texte) est ignoré', async () => {
    const { wrapper } = await mountLayout()
    await wrapper.find('.coque').trigger('dragenter', { dataTransfer: { types: ['text/plain'], files: [] } })
    expect(wrapper.find('.depot').exists()).toBe(false)
  })

  it('affiche le suivi des envois avec avancement et lien vers le document ajouté', async () => {
    const { wrapper } = await mountLayout()
    const uploads = useUploadsStore()
    uploads.items = [
      { id: 1, name: 'facture.pdf', size: 2_500_000, progress: 0.4, status: 'envoi', message: null, documentId: null },
      { id: 2, name: 'devis.pdf', size: 20_000, progress: 1, status: 'termine', message: null, documentId: 42 },
      { id: 3, name: 'doublon.pdf', size: 20_000, progress: 1, status: 'echec', message: 'Doublon de Facture (#5).', documentId: null },
    ]
    await flushPromises()

    const panel = wrapper.find('[aria-label="Envois de documents"]')
    expect(panel.text()).toContain('1 envoi en cours')
    expect(panel.find('[data-upload="envoi"] [role="progressbar"]').attributes('aria-valuenow')).toBe('40')
    expect(panel.find('[data-upload="envoi"]').text()).toContain('2,4 Mo')
    expect(panel.find('[data-upload="termine"] a').attributes('href')).toBe('/documents/42')
    expect(panel.find('[data-upload="echec"]').text()).toContain('Doublon de Facture (#5).')
    expect(panel.find('[aria-label="Fermer le suivi des envois"]').exists()).toBe(false)
  })

  it('prévient avant de quitter la page pendant un envoi', async () => {
    await mountLayout()
    const uploads = useUploadsStore()
    const idle = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(idle)
    expect(idle.defaultPrevented).toBe(false)

    uploads.items = [{ id: 1, name: 'a.pdf', size: 1, progress: 0, status: 'envoi', message: null, documentId: null }]
    const busy = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(busy)
    expect(busy.defaultPrevented).toBe(true)
  })
})
