import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

/** Nombre de fiches gardées dans le menu (comme l'interface Paperless). */
export const MAX_OPEN_DOCUMENTS = 5
export const OPEN_DOCUMENTS_STORAGE_KEY = 'homeged.documents-ouverts'

export interface OpenDocument {
  id: number
  title: string
}

interface Stored {
  user: number
  documents: OpenDocument[]
}

function read(user: number): OpenDocument[] {
  try {
    const stored = JSON.parse(sessionStorage.getItem(OPEN_DOCUMENTS_STORAGE_KEY) ?? 'null') as Stored | null
    if (stored?.user !== user || !Array.isArray(stored.documents)) return []
    return stored.documents
      .filter((d) => Number.isInteger(d?.id) && d.id > 0 && typeof d.title === 'string')
      .slice(0, MAX_OPEN_DOCUMENTS)
  } catch {
    return []
  }
}

function write(value: Stored | null) {
  try {
    if (value === null || value.documents.length === 0) sessionStorage.removeItem(OPEN_DOCUMENTS_STORAGE_KEY)
    else sessionStorage.setItem(OPEN_DOCUMENTS_STORAGE_KEY, JSON.stringify(value))
  } catch {
    // Pas de stockage : la liste vaut pour l'onglet en cours.
  }
}

/**
 * Documents ouverts (UI-11) : accès rapide dans le menu aux dernières fiches consultées. Propres à
 * l'onglet et à l'utilisateur (sessionStorage), oubliés à la déconnexion.
 */
export const useOpenDocumentsStore = defineStore('openDocuments', () => {
  const documents = ref<OpenDocument[]>([])
  const user = ref<number | null>(null)

  /** Recharge la liste de l'utilisateur connecté (après connexion ou rechargement de la page). */
  function load(userId: number) {
    if (user.value === userId) return
    user.value = userId
    documents.value = read(userId)
  }

  watch(documents, (list) => user.value !== null && write({ user: user.value, documents: list }), { deep: true })

  /** Fiche ouverte : en tête de liste, la plus ancienne sort au-delà du maximum. */
  function open(document: OpenDocument) {
    documents.value = [
      { id: document.id, title: document.title },
      ...documents.value.filter((d) => d.id !== document.id),
    ].slice(0, MAX_OPEN_DOCUMENTS)
  }

  /** Fiche fermée, ou document supprimé. */
  function close(id: number) {
    documents.value = documents.value.filter((d) => d.id !== id)
  }

  function closeAll() {
    documents.value = []
  }

  /** Déconnexion : rien ne reste pour le prochain utilisateur de l'appareil. */
  function reset() {
    user.value = null
    documents.value = []
    write(null)
  }

  return { documents, load, open, close, closeAll, reset }
})
