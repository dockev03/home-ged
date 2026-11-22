import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { getTask, MAX_UPLOAD_BYTES, uploadDocument } from '../api/documents'
import { ApiError } from '../api/http'

/** attente → envoi → traitement (OCR et classement par Paperless) → terminé ; ou échec, annulé. */
export type UploadStatus = 'attente' | 'envoi' | 'traitement' | 'termine' | 'echec' | 'annule'

export interface Upload {
  id: number
  name: string
  size: number
  /** Avancement de l'envoi, de 0 à 1. */
  progress: number
  status: UploadStatus
  message: string | null
  /** Document créé par Paperless, une fois le traitement terminé. */
  documentId: number | null
}

/** Suivi du traitement Paperless : une lecture toutes les 2 s, 30 min au plus (gros PDF à reconnaître). */
export const POLL_MS = 2000
const POLL_MAX = 900

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * Envois de fichiers vers Paperless (PWA-07), un à la fois pour ménager la connexion mobile.
 * La file survit à la navigation entre les écrans ; quitter la page pendant un envoi est signalé.
 */
export const useUploadsStore = defineStore('uploads', () => {
  const items = ref<Upload[]>([])
  /** Incrémenté à chaque document ajouté : la liste des documents se recharge. */
  const added = ref(0)
  let nextId = 1
  let running = false
  const controllers = new Map<number, AbortController>()
  /** Fichiers en attente d'envoi, dans l'ordre d'ajout. */
  const queue = new Map<number, File>()

  const active = computed(() => items.value.some((u) => u.status === 'attente' || u.status === 'envoi'))
  const pending = computed(() => items.value.some((u) => ['attente', 'envoi', 'traitement'].includes(u.status)))

  function update(id: number, patch: Partial<Upload>) {
    items.value = items.value.map((u) => (u.id === id ? { ...u, ...patch } : u))
  }

  function add(files: Iterable<File>) {
    for (const file of files) {
      const error =
        file.size > MAX_UPLOAD_BYTES ? 'Fichier trop volumineux (200 Mo au plus).' : file.size === 0 ? 'Fichier vide.' : null
      const upload: Upload = {
        id: nextId++,
        name: file.name,
        size: file.size,
        progress: 0,
        status: error ? 'echec' : 'attente',
        message: error,
        documentId: null,
      }
      items.value = [...items.value, upload]
      if (!error) queue.set(upload.id, file)
    }
    void pump()
  }

  /** Envoie les fichiers en attente, dans l'ordre ; le suivi du traitement se fait en parallèle. */
  async function pump() {
    if (running) return
    running = true
    try {
      for (let next = queue.entries().next(); !next.done; next = queue.entries().next()) {
        const [id, file] = next.value
        queue.delete(id)
        const task = await send(id, file)
        if (task) void follow(id, task)
      }
    } finally {
      running = false
    }
  }

  async function send(id: number, file: File): Promise<string | null> {
    const controller = new AbortController()
    controllers.set(id, controller)
    update(id, { status: 'envoi', progress: 0 })
    try {
      const task = await uploadDocument(file, (progress) => update(id, { progress }), controller.signal)
      update(id, { status: 'traitement', progress: 1 })
      return task
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') update(id, { status: 'annule', message: null })
      else
        update(id, {
          status: 'echec',
          message: e instanceof ApiError ? e.message : 'Envoi interrompu, vérifiez votre connexion.',
        })
      return null
    } finally {
      controllers.delete(id)
    }
  }

  /** Suit la tâche Paperless jusqu'au document créé ; une panne passagère du BFF ne l'arrête pas. */
  async function follow(id: number, task: string) {
    for (let attempt = 0; attempt < POLL_MAX; attempt++) {
      if (!items.value.some((u) => u.id === id)) return
      try {
        const state = await getTask(task)
        if (state.status === 'success') {
          update(id, { status: 'termine', documentId: state.document_id })
          added.value++
          return
        }
        if (state.status === 'failure' || state.status === 'revoked') {
          update(id, { status: 'echec', message: state.message ?? 'Paperless n’a pas pu traiter ce fichier.' })
          return
        }
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return
      }
      await wait(POLL_MS)
    }
    update(id, { status: 'termine', message: 'Traitement long : le document apparaîtra une fois traité par Paperless.' })
  }

  /** Annule un envoi en attente ou en cours ; le traitement déjà confié à Paperless continue. */
  function cancel(id: number) {
    if (queue.delete(id)) update(id, { status: 'annule' })
    controllers.get(id)?.abort()
  }

  /** Retire de la liste les envois finis (terminés, échoués, annulés). */
  function clearFinished() {
    items.value = items.value.filter((u) => ['attente', 'envoi', 'traitement'].includes(u.status))
  }

  /** Déconnexion : abandonne les envois et oublie la liste. */
  function reset() {
    queue.clear()
    for (const controller of controllers.values()) controller.abort()
    items.value = []
  }

  return { items, added, active, pending, add, cancel, clearFinished, reset }
})
