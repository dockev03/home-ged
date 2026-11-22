import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { fullFrame, type Quad } from '../scan/geometry'
import CornerEditor from './CornerEditor.vue'

/** Image de 1000 × 500 affichée sur 500 × 250 px à l'écran. */
function mountEditor(corners: Quad = fullFrame(1000, 500)) {
  const wrapper = mount(CornerEditor, {
    props: {
      src: 'blob:photo',
      width: 1000,
      height: 500,
      modelValue: corners,
      'onUpdate:modelValue': (value: Quad) => wrapper.setProps({ modelValue: value }),
    },
    attachTo: document.body,
  })
  const svg = wrapper.find('svg').element
  svg.getBoundingClientRect = () => ({ left: 100, top: 50, width: 500, height: 250, right: 600, bottom: 300, x: 100, y: 50, toJSON: () => ({}) })
  return wrapper
}

const pointer = (type: string, clientX: number, clientY: number) =>
  Object.assign(new MouseEvent(type, { bubbles: true, cancelable: true, clientX, clientY }), { pointerId: 1 })

const model = (wrapper: ReturnType<typeof mountEditor>) => wrapper.props('modelValue') as Quad

describe('CornerEditor', () => {
  it('affiche les 4 poignées, nommées pour les lecteurs d’écran', () => {
    const wrapper = mountEditor()

    const handles = wrapper.findAll('[data-coin]')
    expect(handles).toHaveLength(4)
    expect(handles[0]!.attributes('aria-label')).toBe('Coin en haut à gauche : déplacer avec les flèches')
    expect(wrapper.find('polygon').attributes('points')).toBe('0,0 1000,0 1000,500 0,500')
    wrapper.unmount()
  })

  it('déplace un coin au doigt, en coordonnées de l’image', async () => {
    const wrapper = mountEditor()
    const svg = wrapper.find('svg').element

    wrapper.find('[data-coin="0"]').element.dispatchEvent(pointer('pointerdown', 100, 50))
    // 50 px à droite et 25 px plus bas à l'écran = 100 et 50 dans l'image (échelle 2).
    svg.dispatchEvent(pointer('pointermove', 150, 75))
    svg.dispatchEvent(pointer('pointerup', 150, 75))
    svg.dispatchEvent(pointer('pointermove', 400, 200))
    await wrapper.vm.$nextTick()

    expect(model(wrapper)[0]).toEqual({ x: 100, y: 50 })
    wrapper.unmount()
  })

  it('garde le coin dans l’image', async () => {
    const wrapper = mountEditor()
    const svg = wrapper.find('svg').element

    wrapper.find('[data-coin="2"]').element.dispatchEvent(pointer('pointerdown', 600, 300))
    svg.dispatchEvent(pointer('pointermove', 900, 0))
    await wrapper.vm.$nextTick()

    expect(model(wrapper)[2]).toEqual({ x: 1000, y: 0 })
    wrapper.unmount()
  })

  it('se règle au clavier : 1 % par appui, 5 % avec Maj', async () => {
    const wrapper = mountEditor()
    const handle = wrapper.find('[data-coin="0"]')

    await handle.trigger('keydown', { key: 'ArrowRight' })
    await handle.trigger('keydown', { key: 'ArrowDown', shiftKey: true })
    await handle.trigger('keydown', { key: 'a' })

    expect(model(wrapper)[0]).toEqual({ x: 10, y: 50 })
    wrapper.unmount()
  })
})
