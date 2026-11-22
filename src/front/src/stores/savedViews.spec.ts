import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'
import { useSavedViewsStore } from './savedViews'

const view = (id: number, name: string) => ({ id, name, filter_rules: [], sort_field: null, sort_reverse: false, owner: 1 })

function stubBff(list: unknown, failWrite = false) {
  const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method && init.method !== 'GET') {
      return failWrite
        ? Response.json({ title: 'Vous n’avez pas le droit de modifier cette vue dans Paperless.' }, { status: 403 })
        : Response.json(view(9, 'Nouvelle'), { status: 201 })
    }
    return Response.json(list)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('savedViews', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.unstubAllGlobals())

  it('charge une fois les vues, et celles du tableau de bord et du menu dans l’ordre', async () => {
    const fetchMock = stubBff({ vues: [view(2, 'A'), view(5, 'B'), view(7, 'C')], tableau_de_bord: [7, 2], menu: [5, 42] })
    const store = useSavedViewsStore()

    await Promise.all([store.ensureLoaded(), store.ensureLoaded()])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(store.dashboard.map((v) => v.name)).toEqual(['C', 'A'])
    expect(store.sidebar.map((v) => v.name)).toEqual(['B'])
    expect(store.onDashboard(2)).toBe(true)
    expect(store.inSidebar(2)).toBe(false)
  })

  it('recharge la liste après une création, même refusée', async () => {
    const fetchMock = stubBff({ vues: [], tableau_de_bord: [], menu: [] }, true)
    const store = useSavedViewsStore()
    await store.ensureLoaded()

    await expect(store.create({ name: 'x' })).rejects.toThrow('pas le droit')

    expect(fetchMock.mock.calls.filter(([input]) => input === '/api/vues')).toHaveLength(3)
  })

  it('signale un échec de chargement et réessaie au prochain appel', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))
    const store = useSavedViewsStore()

    await store.ensureLoaded()
    expect(store.error).toContain('vues sauvegardées')

    stubBff({ vues: [view(1, 'A')], tableau_de_bord: [], menu: [] })
    await store.ensureLoaded()
    expect(store.error).toBeNull()
    expect(store.views).toHaveLength(1)
  })

  it('oublie les vues à la déconnexion', async () => {
    stubBff({ vues: [view(1, 'A')], tableau_de_bord: [1], menu: [1] })
    const store = useSavedViewsStore()
    await store.ensureLoaded()

    useAuthStore().clear()

    expect(store.views).toEqual([])
    expect(store.dashboard).toEqual([])
  })
})
