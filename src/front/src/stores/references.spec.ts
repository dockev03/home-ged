import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useReferencesStore } from './references'

describe('store references', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('charge les cinq listes une seule fois et indexe par identifiant', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      String(input).endsWith('/tags')
        ? Response.json([{ id: 3, name: 'Banque', color: '#000', text_color: '#fff', is_inbox_tag: false }])
        : Response.json([]),
    )
    vi.stubGlobal('fetch', fetchMock)
    const store = useReferencesStore()

    await Promise.all([store.ensureLoaded(), store.ensureLoaded()])

    expect(fetchMock).toHaveBeenCalledTimes(5)
    expect(store.tags.get(3)?.name).toBe('Banque')
  })

  it("signale l'échec et réessaie au prochain appel", async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))
    const store = useReferencesStore()

    await store.ensureLoaded()
    expect(store.error).not.toBeNull()

    vi.stubGlobal('fetch', vi.fn(async () => Response.json([])))
    await store.ensureLoaded()
    expect(store.error).toBeNull()
  })

  it('oublie les listes à la déconnexion', () => {
    const store = useReferencesStore()
    store.lists.tags = [{ id: 1, name: 'x', color: '', text_color: '', is_inbox_tag: false }]
    store.reset()
    expect(store.lists.tags).toEqual([])
  })
})
