import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defaultFilters, type DocumentFilters } from '../filters/documentFilters'
import { useReferencesStore } from '../stores/references'
import FilterBar from './FilterBar.vue'

function mountBar(overrides: Partial<DocumentFilters> = {}) {
  return mount(FilterBar, { props: { modelValue: { ...defaultFilters(), ...overrides } }, attachTo: document.body })
}

type Wrapper = ReturnType<typeof mountBar>

const lastEmitted = (wrapper: Wrapper) => wrapper.emitted('update:modelValue')!.at(-1)![0] as DocumentFilters

async function openFilter(wrapper: Wrapper, label: string) {
  const toggle = wrapper.findAll('button.dropdown-toggle').find((b) => b.text().startsWith(label))
  expect(toggle, `filtre « ${label} »`).toBeDefined()
  await toggle!.trigger('click')
}

describe('FilterBar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useReferencesStore().lists.tags = [
      { id: 1, name: 'Banque', color: '#a6cee3', text_color: '#000000', is_inbox_tag: false },
      { id: 2, name: 'Impôts', color: '#1f78b4', text_color: '#ffffff', is_inbox_tag: false },
    ]
  })
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('applique le texte après une pause de frappe, ou tout de suite sur Entrée', async () => {
    vi.useFakeTimers()
    const wrapper = mountBar()
    const input = wrapper.find('input[aria-label="Texte recherché"]')

    await input.setValue('edf')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    vi.advanceTimersByTime(400)
    expect(lastEmitted(wrapper).texte).toBe('edf')

    await input.setValue('gaz ')
    await input.trigger('keydown', { key: 'Enter' })
    expect(lastEmitted(wrapper).texte).toBe('gaz')
  })

  it('choisit des étiquettes et leur combinaison', async () => {
    const wrapper = mountBar()
    await openFilter(wrapper, 'Étiquettes')

    const banque = wrapper.findAll('label.dropdown-item').find((l) => l.text() === 'Banque')!
    await banque.find('input').trigger('change')
    expect(lastEmitted(wrapper).tags).toEqual([1])

    await wrapper.findAll('button').find((b) => b.text() === 'Au moins une')!.trigger('click')
    expect(lastEmitted(wrapper).tagsMode).toBe('un')
  })

  it('« Sans étiquette » est exclusif', async () => {
    const wrapper = mountBar({ tags: [1] })
    await openFilter(wrapper, 'Étiquettes')

    const aucun = wrapper.findAll('label.dropdown-item').find((l) => l.text() === 'Sans étiquette')!
    await aucun.find('input').trigger('change')

    expect(lastEmitted(wrapper).tags).toBe('aucun')
  })

  it('annonce le nombre de valeurs choisies', () => {
    const wrapper = mountBar({ tags: [1, 2] })
    const toggle = wrapper.findAll('button.dropdown-toggle').find((b) => b.text().startsWith('Étiquettes'))!
    expect(toggle.attributes('aria-label')).toBe('Étiquettes (2 choisis)')
    expect(toggle.classes()).toContain('btn-primary')
  })

  it('filtre par date et propriétaire', async () => {
    const wrapper = mountBar()
    await openFilter(wrapper, 'Dates')
    await wrapper.find('input[type="date"]').setValue('2025-01-01')
    expect(lastEmitted(wrapper).creeDu).toBe('2025-01-01')

    await openFilter(wrapper, 'Permissions')
    await wrapper.find('#proprio-moi').trigger('change')
    expect(lastEmitted(wrapper).proprietaire).toBe('moi')
  })

  it('inverse le tri', async () => {
    const wrapper = mountBar()
    await wrapper.find('button[aria-label^="Ordre décroissant"]').trigger('click')
    expect(lastEmitted(wrapper)).toMatchObject({ tri: 'created', desc: false })
  })

  it('réinitialise les filtres en gardant le tri', async () => {
    const wrapper = mountBar({ texte: 'edf', tags: [1], tri: 'title', desc: false })

    await wrapper.findAll('button').find((b) => b.text() === 'Réinitialiser les filtres')!.trigger('click')

    expect(lastEmitted(wrapper)).toEqual({ ...defaultFilters(), tri: 'title', desc: false })
    expect((wrapper.find('input[aria-label="Texte recherché"]').element as HTMLInputElement).value).toBe('')
  })

  it("n'affiche pas « Réinitialiser » sans filtre actif", () => {
    const wrapper = mountBar({ tri: 'title' })
    expect(wrapper.text()).not.toContain('Réinitialiser')
  })
})
