import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Quad } from '../scan/geometry'
import { useUploadsStore } from '../stores/uploads'
import ScanView from './ScanView.vue'

const scanner = vi.hoisted(() => ({
  charger: vi.fn(),
  redresser: vi.fn(),
  fermer: vi.fn(),
}))
const images = vi.hoisted(() => ({ decodePhoto: vi.fn(), encodePage: vi.fn() }))
const pdf = vi.hoisted(() => ({ buildPdf: vi.fn() }))

vi.mock('../scan/scanner', () => ({ createScanner: () => scanner }))
vi.mock('../scan/images', () => images)
vi.mock('../scan/pdf', () => pdf)

const DETECTED: Quad = [
  { x: 100, y: 50 },
  { x: 900, y: 60 },
  { x: 880, y: 450 },
  { x: 120, y: 440 },
]

const PAGE = { width: 10, height: 10 } as ImageData

const PERSONNE = {
  id: 2,
  name: 'Personne',
  data_type: 'select',
  extra_data: { select_options: [{ id: 'opt-k', label: 'Kévin' }, { id: 'opt-f', label: 'Foyer' }] },
}
const TAG = { id: 3, name: 'Facture', color: '#a6cee3', text_color: '#000000', is_inbox_tag: false }

let urls = 0
let jpegs = 0
let champs: unknown[] = [PERSONNE]

beforeEach(() => {
  urls = 0
  jpegs = 0
  champs = [PERSONNE]
  setActivePinia(createPinia())
  URL.createObjectURL = vi.fn(() => `blob:page-${++urls}`)
  URL.revokeObjectURL = vi.fn()
  scanner.charger.mockResolvedValue(DETECTED)
  scanner.redresser.mockResolvedValue(PAGE)
  images.decodePhoto.mockImplementation(async () => ({
    image: { width: 1000, height: 500 },
    preview: 'blob:photo',
    width: 1000,
    height: 500,
  }))
  images.encodePage.mockImplementation(async () => new Blob([`jpeg-${++jpegs}`]))
  pdf.buildPdf.mockReturnValue(new Uint8Array([37, 80, 68, 70]))
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => Response.json(url.endsWith('/champs') ? champs : url.endsWith('/tags') ? [TAG] : [])),
  )
})

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

const mounted: { unmount: () => void }[] = []

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/scanner', name: 'scanner', component: ScanView },
      { path: '/documents', name: 'documents', component: { template: '<p>documents</p>' } },
    ],
  })
  await router.push('/scanner')
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router] } })
  mounted.push(wrapper)
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountView>>['wrapper']

async function choosePhoto(wrapper: Wrapper, input = 'camera', name = 'photo.jpg') {
  const element = wrapper.find(`[data-test="${input}"]`).element as HTMLInputElement
  Object.defineProperty(element, 'files', { value: [new File(['x'], name, { type: 'image/jpeg' })], configurable: true })
  element.dispatchEvent(new Event('change'))
  await flushPromises()
}

const button = (wrapper: Wrapper, label: string) => wrapper.findAll('button').find((b) => b.text() === label)

async function click(wrapper: Wrapper, label: string) {
  await button(wrapper, label)!.trigger('click')
  await flushPromises()
}

/** Photographie, recadre et ajoute une page au document. */
async function addPage(wrapper: Wrapper, name = 'photo.jpg') {
  await choosePhoto(wrapper, 'camera', name)
  await click(wrapper, 'Valider le recadrage')
  await click(wrapper, 'Ajouter au document')
}

const thumbnails = (wrapper: Wrapper) => wrapper.findAll('[data-test="page"] img').map((img) => img.attributes('src'))

describe('ScanView', () => {
  it('propose l’appareil photo (caméra arrière) ou une image existante', async () => {
    const { wrapper } = await mountView()

    const camera = wrapper.find('[data-test="camera"]')
    expect(camera.attributes('capture')).toBe('environment')
    expect(camera.attributes('accept')).toBe('image/*')
    expect(wrapper.find('[data-test="galerie"]').attributes('capture')).toBeUndefined()
    expect(button(wrapper, 'Prendre une photo')).toBeDefined()
    expect(button(wrapper, 'Choisir une image')).toBeDefined()
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('place les coins détectés, puis redresse la page avec le filtre « Scan »', async () => {
    const { wrapper } = await mountView()
    await choosePhoto(wrapper)

    expect(scanner.charger).toHaveBeenCalledWith({ width: 1000, height: 500 })
    expect(wrapper.find('[data-test="consigne"]').text()).toBe('Bords détectés : ajustez les coins si besoin.')
    expect(wrapper.find('polygon').attributes('points')).toBe('100,50 900,60 880,450 120,440')

    await click(wrapper, 'Valider le recadrage')

    expect(scanner.redresser).toHaveBeenCalledWith(DETECTED, 'scan')
    expect(wrapper.find('img.page').attributes('src')).toBe('blob:page-1')
    expect(button(wrapper, 'Scan')!.attributes('aria-pressed')).toBe('true')
  })

  it('cadre toute l’image quand aucune page n’est détectée', async () => {
    scanner.charger.mockResolvedValue(null)
    const { wrapper } = await mountView()
    await choosePhoto(wrapper, 'galerie')

    expect(wrapper.find('[data-test="consigne"]').text()).toContain('Bords non détectés')
    expect(wrapper.find('polygon').attributes('points')).toBe('0,0 1000,0 1000,500 0,500')
    expect(button(wrapper, 'Bords détectés')).toBeUndefined()
  })

  it('revient aux bords détectés ou à toute l’image', async () => {
    const { wrapper } = await mountView()
    await choosePhoto(wrapper)

    await click(wrapper, "Toute l'image")
    expect(wrapper.find('polygon').attributes('points')).toBe('0,0 1000,0 1000,500 0,500')
    await click(wrapper, 'Bords détectés')
    expect(wrapper.find('polygon').attributes('points')).toBe('100,50 900,60 880,450 120,440')
  })

  it('refuse des coins croisés', async () => {
    scanner.charger.mockResolvedValue([DETECTED[0], DETECTED[2], DETECTED[1], DETECTED[3]])
    const { wrapper } = await mountView()
    await choosePhoto(wrapper)

    expect(wrapper.text()).toContain('Les coins se croisent')
    expect(button(wrapper, 'Valider le recadrage')!.attributes('disabled')).toBeDefined()
  })

  it('change de filtre et revient au recadrage', async () => {
    const { wrapper } = await mountView()
    await choosePhoto(wrapper)
    await click(wrapper, 'Valider le recadrage')

    await click(wrapper, 'Niveaux de gris')

    expect(scanner.redresser).toHaveBeenLastCalledWith(DETECTED, 'gris')
    expect(wrapper.find('img.page').attributes('src')).toBe('blob:page-2')
    // L'aperçu précédent est libéré.
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-1')

    await click(wrapper, 'Recadrer')
    expect(wrapper.find('polygon').exists()).toBe(true)
  })

  it('affiche l’erreur du module de recadrage', async () => {
    scanner.charger.mockRejectedValue(new Error('Le module de recadrage n’a pas pu démarrer.'))
    const { wrapper } = await mountView()
    await choosePhoto(wrapper)

    expect(wrapper.find('[role="alert"]').text()).toBe('Le module de recadrage n’a pas pu démarrer.')
    expect(button(wrapper, 'Prendre une photo')).toBeDefined()
  })

  it('ajoute les pages au document, dans l’ordre, et les réordonne (SCAN-04)', async () => {
    const { wrapper } = await mountView()
    await addPage(wrapper)
    await addPage(wrapper)

    expect(thumbnails(wrapper)).toEqual(['blob:page-1', 'blob:page-2'])
    expect(button(wrapper, 'Envoyer 2 pages')).toBeDefined()
    // La page ajoutée garde son URL : elle n'est pas libérée.
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith('blob:page-1')

    await wrapper.find('[aria-label="Déplacer la page 2 avant"]').trigger('click')
    expect(thumbnails(wrapper)).toEqual(['blob:page-2', 'blob:page-1'])
    expect(wrapper.find('[aria-label="Déplacer la page 1 avant"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[aria-label="Déplacer la page 2 après"]').attributes('disabled')).toBeDefined()
  })

  it('recadre à nouveau une page avec ses coins et son filtre, puis la remplace', async () => {
    const { wrapper } = await mountView()
    await addPage(wrapper, 'premiere.jpg')
    await choosePhoto(wrapper, 'camera', 'seconde.jpg')
    await click(wrapper, 'Valider le recadrage')
    await click(wrapper, 'Couleur')
    await click(wrapper, 'Ajouter au document')

    await wrapper.find('[aria-label="Recadrer la page 2"]').trigger('click')
    await flushPromises()

    expect((images.decodePhoto.mock.lastCall![0] as File).name).toBe('seconde.jpg')
    expect(wrapper.find('[data-test="consigne"]').text()).toContain('Page 2 :')
    expect(button(wrapper, 'Reprendre la photo')).toBeUndefined()

    await click(wrapper, 'Valider le recadrage')
    expect(scanner.redresser).toHaveBeenLastCalledWith(DETECTED, 'couleur')
    await click(wrapper, 'Remplacer la page 2')

    expect(thumbnails(wrapper)).toEqual(['blob:page-1', 'blob:page-4'])
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-3')
  })

  it('abandonne la page en préparation sans toucher au document', async () => {
    const { wrapper } = await mountView()
    await addPage(wrapper)
    await choosePhoto(wrapper)

    await click(wrapper, 'Retour au document')

    expect(thumbnails(wrapper)).toEqual(['blob:page-1'])
  })

  it('supprime une page après confirmation', async () => {
    const { wrapper } = await mountView()
    await addPage(wrapper)
    await addPage(wrapper)

    await wrapper.find('[aria-label="Supprimer la page 1"]').trigger('click')
    expect(wrapper.find('.modal-title').text()).toBe('Supprimer la page 1 ?')
    await wrapper.find('.modal-footer .btn-danger').trigger('click')

    expect(thumbnails(wrapper)).toEqual(['blob:page-2'])
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-1')
  })

  it('assemble le PDF et l’envoie avec titre, personne et étiquettes (SCAN-05, 06)', async () => {
    const { wrapper } = await mountView()
    const uploads = useUploadsStore()
    const add = vi.spyOn(uploads, 'add').mockImplementation(() => {})
    await addPage(wrapper)
    await addPage(wrapper)
    await wrapper.find('[aria-label="Déplacer la page 2 avant"]').trigger('click')

    await wrapper.find('#scan-titre').setValue('Facture EDF : novembre')
    await wrapper.find('#scan-personne').setValue('opt-k')
    await wrapper.find('#scan-etiquettes').setValue('3')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const pages = (pdf.buildPdf.mock.lastCall![0] as Uint8Array[]).map((bytes) => new TextDecoder().decode(bytes))
    expect(pages).toEqual(['jpeg-2', 'jpeg-1'])
    const [files, metadata] = add.mock.lastCall!
    const file = [...files][0]!
    expect(file.name).toBe('Facture EDF - novembre.pdf')
    expect(file.type).toBe('application/pdf')
    expect(metadata).toEqual({ title: 'Facture EDF : novembre', tags: [3], customFields: { 2: 'opt-k' } })

    expect(wrapper.find('[data-test="envoye"]').text()).toContain('Document de 2 pages envoyé.')
    expect(thumbnails(wrapper)).toEqual([])
    expect(button(wrapper, 'Prendre une photo')).toBeDefined()
  })

  it('envoie sans métadonnées quand rien n’est saisi, et masque « Personne » si le champ n’existe pas', async () => {
    champs = []
    const { wrapper } = await mountView()
    const add = vi.spyOn(useUploadsStore(), 'add').mockImplementation(() => {})
    await addPage(wrapper)

    expect(wrapper.find('#scan-personne').exists()).toBe(false)
    expect(button(wrapper, 'Envoyer 1 page')!.attributes('type')).toBe('submit')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const [files, metadata] = add.mock.lastCall!
    expect([...files][0]!.name).toMatch(/^Scan du \d{2}-\d{2}-\d{4} à \d{2}h\d{2}\.pdf$/)
    expect(metadata).toEqual({ title: '', tags: [], customFields: {} })
  })

  it('signale un PDF impossible à assembler et garde les pages', async () => {
    pdf.buildPdf.mockImplementation(() => {
      throw new Error('Une page n’est pas une image JPEG valide.')
    })
    const { wrapper } = await mountView()
    const add = vi.spyOn(useUploadsStore(), 'add')
    await addPage(wrapper)

    expect(button(wrapper, 'Envoyer 1 page')!.attributes('type')).toBe('submit')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toBe('Une page n’est pas une image JPEG valide.')
    expect(add).not.toHaveBeenCalled()
    expect(thumbnails(wrapper)).toHaveLength(1)
  })

  it('demande confirmation avant de quitter avec des pages non envoyées', async () => {
    const { wrapper, router } = await mountView()
    await addPage(wrapper)

    await router.push('/documents')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/scanner')
    expect(wrapper.find('.modal-title').text()).toBe('Document non envoyé')

    await click(wrapper, 'Quitter sans envoyer')
    expect(router.currentRoute.value.path).toBe('/documents')
    expect(scanner.fermer).toHaveBeenCalled()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-1')
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:photo')
  })

  it('quitte sans confirmation quand rien n’est en cours', async () => {
    const { router } = await mountView()

    await router.push('/documents')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/documents')
    expect(scanner.fermer).toHaveBeenCalled()
  })
})
