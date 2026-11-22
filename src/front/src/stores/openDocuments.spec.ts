import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { MAX_OPEN_DOCUMENTS, OPEN_DOCUMENTS_STORAGE_KEY, useOpenDocumentsStore } from './openDocuments'

describe('documents ouverts', () => {
  beforeEach(() => {
    sessionStorage.clear()
    setActivePinia(createPinia())
  })

  it('met la dernière fiche en tête, sans doublon, dans la limite du maximum', () => {
    const store = useOpenDocumentsStore()
    store.load(1)
    for (let id = 1; id <= MAX_OPEN_DOCUMENTS + 1; id++) store.open({ id, title: `Doc ${id}` })
    store.open({ id: 4, title: 'Doc 4 renommé' })

    expect(store.documents.map((d) => d.id)).toEqual([4, 6, 5, 3, 2])
    expect(store.documents[0]!.title).toBe('Doc 4 renommé')
  })

  it('ferme une fiche ou toutes', () => {
    const store = useOpenDocumentsStore()
    store.load(1)
    store.open({ id: 1, title: 'A' })
    store.open({ id: 2, title: 'B' })
    store.close(1)
    expect(store.documents.map((d) => d.id)).toEqual([2])
    store.closeAll()
    expect(store.documents).toEqual([])
  })

  it("survit au rechargement de l'onglet, pour le même utilisateur seulement", async () => {
    const store = useOpenDocumentsStore()
    store.load(1)
    store.open({ id: 7, title: 'Facture' })
    await nextTick()

    setActivePinia(createPinia())
    const reloaded = useOpenDocumentsStore()
    reloaded.load(1)
    expect(reloaded.documents).toEqual([{ id: 7, title: 'Facture' }])

    setActivePinia(createPinia())
    const other = useOpenDocumentsStore()
    other.load(2)
    expect(other.documents).toEqual([])
  })

  it('ignore un stockage corrompu', () => {
    sessionStorage.setItem(OPEN_DOCUMENTS_STORAGE_KEY, '{"user":1,"documents":[{"id":"x"},{"id":3,"title":"ok"}]}')
    const store = useOpenDocumentsStore()
    store.load(1)
    expect(store.documents).toEqual([{ id: 3, title: 'ok' }])

    sessionStorage.setItem(OPEN_DOCUMENTS_STORAGE_KEY, 'pas du json')
    setActivePinia(createPinia())
    const broken = useOpenDocumentsStore()
    broken.load(1)
    expect(broken.documents).toEqual([])
  })

  it('oublie tout à la déconnexion', async () => {
    const store = useOpenDocumentsStore()
    store.load(1)
    store.open({ id: 7, title: 'Facture' })
    await nextTick()
    store.reset()
    await nextTick()

    expect(store.documents).toEqual([])
    expect(sessionStorage.getItem(OPEN_DOCUMENTS_STORAGE_KEY)).toBeNull()
  })
})
