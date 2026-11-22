import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Quad } from '../scan/geometry'
import ScanView from './ScanView.vue'

const scanner = vi.hoisted(() => ({
  charger: vi.fn(),
  redresser: vi.fn(),
  fermer: vi.fn(),
}))

vi.mock('../scan/scanner', () => ({ createScanner: () => scanner }))
vi.mock('../scan/images', () => ({
  decodePhoto: vi.fn(async () => ({ image: { width: 1000, height: 500 }, preview: 'blob:photo', width: 1000, height: 500 })),
  encodePage: vi.fn(async () => new Blob(['jpeg'])),
}))

const DETECTED: Quad = [
  { x: 100, y: 50 },
  { x: 900, y: 60 },
  { x: 880, y: 450 },
  { x: 120, y: 440 },
]

const PAGE = { width: 10, height: 10 } as ImageData

let urls = 0

beforeEach(() => {
  urls = 0
  URL.createObjectURL = vi.fn(() => `blob:page-${++urls}`)
  URL.revokeObjectURL = vi.fn()
  scanner.charger.mockResolvedValue(DETECTED)
  scanner.redresser.mockResolvedValue(PAGE)
})

afterEach(() => vi.clearAllMocks())

async function choosePhoto(wrapper: ReturnType<typeof mount>, input = 'camera') {
  const element = wrapper.find(`[data-test="${input}"]`).element as HTMLInputElement
  Object.defineProperty(element, 'files', { value: [new File(['x'], 'photo.jpg', { type: 'image/jpeg' })], configurable: true })
  element.dispatchEvent(new Event('change'))
  await flushPromises()
}

const button = (wrapper: ReturnType<typeof mount>, label: string) => wrapper.findAll('button').find((b) => b.text() === label)!

describe('ScanView', () => {
  it('propose l’appareil photo (caméra arrière) ou une image existante', () => {
    const wrapper = mount(ScanView)

    const camera = wrapper.find('[data-test="camera"]')
    expect(camera.attributes('capture')).toBe('environment')
    expect(camera.attributes('accept')).toBe('image/*')
    expect(wrapper.find('[data-test="galerie"]').attributes('capture')).toBeUndefined()
    expect(button(wrapper, 'Prendre une photo').exists()).toBe(true)
    expect(button(wrapper, 'Choisir une image').exists()).toBe(true)
  })

  it('place les coins détectés, puis redresse la page avec le filtre « Scan »', async () => {
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)

    expect(scanner.charger).toHaveBeenCalledWith({ width: 1000, height: 500 })
    expect(wrapper.find('[data-test="consigne"]').text()).toBe('Bords détectés : ajustez les coins si besoin.')
    expect(wrapper.find('polygon').attributes('points')).toBe('100,50 900,60 880,450 120,440')

    await button(wrapper, 'Valider le recadrage').trigger('click')
    await flushPromises()

    expect(scanner.redresser).toHaveBeenCalledWith(DETECTED, 'scan')
    expect(wrapper.find('img.page').attributes('src')).toBe('blob:page-1')
    expect(button(wrapper, 'Scan').attributes('aria-pressed')).toBe('true')
  })

  it('cadre toute l’image quand aucune page n’est détectée', async () => {
    scanner.charger.mockResolvedValue(null)
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper, 'galerie')

    expect(wrapper.find('[data-test="consigne"]').text()).toContain('Bords non détectés')
    expect(wrapper.find('polygon').attributes('points')).toBe('0,0 1000,0 1000,500 0,500')
    expect(button(wrapper, 'Bords détectés')).toBeUndefined()
  })

  it('revient aux bords détectés ou à toute l’image', async () => {
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)

    await button(wrapper, "Toute l'image").trigger('click')
    expect(wrapper.find('polygon').attributes('points')).toBe('0,0 1000,0 1000,500 0,500')
    await button(wrapper, 'Bords détectés').trigger('click')
    expect(wrapper.find('polygon').attributes('points')).toBe('100,50 900,60 880,450 120,440')
  })

  it('refuse des coins croisés', async () => {
    scanner.charger.mockResolvedValue([DETECTED[0], DETECTED[2], DETECTED[1], DETECTED[3]])
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)

    expect(wrapper.text()).toContain('Les coins se croisent')
    expect(button(wrapper, 'Valider le recadrage').attributes('disabled')).toBeDefined()
  })

  it('change de filtre et revient au recadrage', async () => {
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)
    await button(wrapper, 'Valider le recadrage').trigger('click')
    await flushPromises()

    await button(wrapper, 'Niveaux de gris').trigger('click')
    await flushPromises()

    expect(scanner.redresser).toHaveBeenLastCalledWith(DETECTED, 'gris')
    expect(wrapper.find('img.page').attributes('src')).toBe('blob:page-2')
    // L'aperçu précédent est libéré.
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-1')

    await button(wrapper, 'Recadrer').trigger('click')
    expect(wrapper.find('polygon').exists()).toBe(true)
  })

  it('affiche l’erreur du module de recadrage', async () => {
    scanner.charger.mockRejectedValue(new Error('Le module de recadrage n’a pas pu démarrer.'))
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)

    expect(wrapper.find('[role="alert"]').text()).toBe('Le module de recadrage n’a pas pu démarrer.')
    expect(button(wrapper, 'Prendre une photo').exists()).toBe(true)
  })

  it('arrête le worker et libère les images en quittant l’écran', async () => {
    const wrapper = mount(ScanView)
    await choosePhoto(wrapper)
    await button(wrapper, 'Valider le recadrage').trigger('click')
    await flushPromises()

    wrapper.unmount()

    expect(scanner.fermer).toHaveBeenCalled()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:page-1')
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:photo')
  })
})
