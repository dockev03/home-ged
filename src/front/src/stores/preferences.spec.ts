import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { THEME_STORAGE_KEY, usePreferencesStore } from './preferences'

describe('store preferences', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })
  afterEach(() => vi.restoreAllMocks())

  it('suit le système par défaut', () => {
    expect(usePreferencesStore().theme).toBe('systeme')
  })

  it('relit le choix mémorisé', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'sombre')
    expect(usePreferencesStore().theme).toBe('sombre')
  })

  it('ignore une valeur inconnue', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'violet')
    expect(usePreferencesStore().theme).toBe('systeme')
  })

  it('mémorise le choix, et revenir au système efface la clé', async () => {
    const preferences = usePreferencesStore()

    preferences.theme = 'clair'
    await nextTick()
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('clair')

    preferences.theme = 'systeme'
    await nextTick()
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull()
  })

  it('fonctionne sans stockage (navigation privée, stockage bloqué)', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('bloqué', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('bloqué', 'SecurityError')
    })
    const preferences = usePreferencesStore()
    expect(preferences.theme).toBe('systeme')

    preferences.theme = 'sombre'
    await nextTick()
    expect(preferences.theme).toBe('sombre')
  })
})
