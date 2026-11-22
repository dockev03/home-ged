import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { getTask, MAX_UPLOAD_BYTES, uploadDocument, type UploadMetadata } from '../api/documents'
import { ApiError } from '../api/http'
import { isNetworkError } from '../offline/db'
import { listQueuedUploads, removeQueuedUpload, saveQueuedUpload } from '../offline/uploads'
import { useNetworkStore } from './network'

/**
 * attente → envoi → traitement (OCR et classement par Paperless) → terminé ; ou échec, annulé.
 * « reseau » : en attente du réseau (ou d'une reconnexion), gardé sur l'appareil (PWA-10).
 */
export type UploadStatus = 'attente' | 'reseau' | 'envoi' | 'traitement' | 'termine' | 'echec' | 'annule'

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
/** Serveur injoignable alors que l'appareil se dit en ligne : nouvel essai après ce délai. */
export const RETRY_MS = 30_000

const OFFLINE_MESSAGE = 'Hors ligne : l’envoi partira au retour du réseau.'
const SESSION_MESSAGE = 'Session expirée : l’envoi partira après reconnexion.'

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

const newKey = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`

interface QueueEntry {
  /** Clé de l'envoi gardé sur l'appareil. */
  key: string
  file: File
  metadata: UploadMetadata
  /** Enregistrement sur l'appareil en cours : à attendre avant de le retirer. */
  saved: Promise<boolean>
}

/**
 * Envois de fichiers vers Paperless (PWA-07), un à la fois pour ménager la connexion mobile.
 * Chaque fichier est gardé sur l'appareil jusqu'à sa réception par Paperless (PWA-10) : hors
 * ligne, il attend le retour du réseau, même si l'application est fermée entre-temps.
 */
export const useUploadsStore = defineStore('uploads', () => {
  const network = useNetworkStore()
  const items = ref<Upload[]>([])
  /** Incrémenté à chaque document ajouté : la liste des documents se recharge. */
  const added = ref(0)
  /** Utilisateur à qui appartiennent les envois gardés sur l'appareil. */
  let owner: number | null = null
  let nextId = 1
  let running = false
  let retryTimer: ReturnType<typeof setTimeout> | null = null
  const controllers = new Map<number, AbortController>()
  /** Fichiers à envoyer (en attente ou en attente du réseau), dans l'ordre d'ajout. */
  const queue = new Map<number, QueueEntry>()

  /** Envoi en cours ou imminent : quitter la page l'interromprait (les envois « reseau » sont gardés). */
  const active = computed(() => items.value.some((u) => u.status === 'attente' || u.status === 'envoi'))
  const pending = computed(() => items.value.some((u) => ['attente', 'reseau', 'envoi', 'traitement'].includes(u.status)))
  /** Fichiers pas encore reçus par Paperless. */
  const waiting = computed(() => items.value.filter((u) => ['attente', 'reseau', 'envoi'].includes(u.status)).length)

  function update(id: number, patch: Partial<Upload>) {
    items.value = items.value.map((u) => (u.id === id ? { ...u, ...patch } : u))
  }

  function enqueue(file: File, metadata: UploadMetadata, key: string, saved: Promise<boolean>, status: UploadStatus) {
    const upload: Upload = {
      id: nextId++,
      name: file.name,
      size: file.size,
      progress: 0,
      status,
      message: status === 'reseau' ? OFFLINE_MESSAGE : null,
      documentId: null,
    }
    items.value = [...items.value, upload]
    queue.set(upload.id, { key, file, metadata, saved })
  }

  /** Ajoute des fichiers à la file ; les métadonnées (SCAN-06) s'appliquent à chacun. */
  function add(files: Iterable<File>, metadata: UploadMetadata = {}) {
    for (const file of files) {
      const error =
        file.size > MAX_UPLOAD_BYTES ? 'Fichier trop volumineux (200 Mo au plus).' : file.size === 0 ? 'Fichier vide.' : null
      if (error) {
        items.value = [
          ...items.value,
          { id: nextId++, name: file.name, size: file.size, progress: 0, status: 'echec', message: error, documentId: null },
        ]
        continue
      }
      const key = newKey()
      const saved = owner === null ? Promise.resolve(false) : saveQueuedUpload(owner, { key, file, metadata })
      enqueue(file, metadata, key, saved, network.online ? 'attente' : 'reseau')
    }
    void pump()
  }

  /** Fichier reçu par Paperless, annulé ou refusé : il n'est plus gardé sur l'appareil. */
  async function forget(entry: QueueEntry) {
    if (await entry.saved) await removeQueuedUpload(entry.key)
  }

  /** Envoie les fichiers en attente, dans l'ordre ; le suivi du traitement se fait en parallèle. */
  async function pump() {
    if (running) return
    running = true
    try {
      for (;;) {
        const next = [...queue.entries()].find(([id]) => items.value.find((u) => u.id === id)?.status === 'attente')
        if (!next) break
        const [id, entry] = next
        if (!network.online) {
          setWaiting(id, OFFLINE_MESSAGE)
          continue
        }
        const task = await send(id, entry)
        if (task) void follow(id, task)
      }
    } finally {
      running = false
    }
  }

  function setWaiting(id: number, message: string) {
    update(id, { status: 'reseau', progress: 0, message })
  }

  async function send(id: number, entry: QueueEntry): Promise<string | null> {
    const controller = new AbortController()
    controllers.set(id, controller)
    update(id, { status: 'envoi', progress: 0, message: null })
    try {
      const task = await uploadDocument(entry.file, (progress) => update(id, { progress }), controller.signal, entry.metadata)
      queue.delete(id)
      update(id, { status: 'traitement', progress: 1 })
      void forget(entry)
      return task
    } catch (e) {
      // File abandonnée (fin de session) : le fichier gardé sur l'appareil reste à l'appelant.
      if (!queue.has(id)) return null
      if (e instanceof DOMException && e.name === 'AbortError') {
        queue.delete(id)
        update(id, { status: 'annule', message: null })
        void forget(entry)
      } else if (isNetworkError(e)) {
        setWaiting(id, OFFLINE_MESSAGE)
        scheduleRetry()
      } else if (e instanceof ApiError && e.status === 401) {
        setWaiting(id, SESSION_MESSAGE)
      } else {
        queue.delete(id)
        update(id, { status: 'echec', message: e instanceof ApiError ? e.message : 'Envoi refusé.' })
        void forget(entry)
      }
      return null
    } finally {
      controllers.delete(id)
    }
  }

  /** Relance les envois en attente du réseau (retour du réseau, reconnexion, nouvel essai). */
  function resume() {
    if (retryTimer !== null) {
      clearTimeout(retryTimer)
      retryTimer = null
    }
    let resumed = false
    for (const u of items.value) {
      if (u.status === 'reseau' && queue.has(u.id)) {
        update(u.id, { status: 'attente', message: null })
        resumed = true
      }
    }
    if (resumed) void pump()
  }

  function scheduleRetry() {
    if (retryTimer !== null || !network.online) return
    retryTimer = setTimeout(() => {
      retryTimer = null
      resume()
    }, RETRY_MS)
  }

  network.onReconnect(resume)

  /**
   * Session ouverte (ou reprise hors ligne) : les envois gardés sur l'appareil pour cet utilisateur
   * reprennent leur place dans la file, et partent si le réseau est là.
   */
  async function restore(userId: number) {
    owner = userId
    const known = new Set([...queue.values()].map((e) => e.key))
    const stored = await listQueuedUploads(userId)
    if (owner !== userId) return
    for (const upload of stored) {
      if (!known.has(upload.key)) enqueue(upload.file, upload.metadata, upload.key, Promise.resolve(true), 'reseau')
    }
    resume()
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
    const entry = queue.get(id)
    const controller = controllers.get(id)
    if (controller) controller.abort()
    else if (entry) {
      queue.delete(id)
      update(id, { status: 'annule', message: null })
      void forget(entry)
    }
  }

  /** Retire de la liste les envois finis (terminés, échoués, annulés). */
  function clearFinished() {
    items.value = items.value.filter((u) => ['attente', 'reseau', 'envoi', 'traitement'].includes(u.status))
  }

  /**
   * Fin de session : la file en mémoire est abandonnée. Ce qui est gardé sur l'appareil est effacé
   * ou conservé par l'appelant (déconnexion ou session expirée, voir offline/session.ts).
   */
  function reset() {
    owner = null
    queue.clear()
    for (const controller of controllers.values()) controller.abort()
    if (retryTimer !== null) clearTimeout(retryTimer)
    retryTimer = null
    items.value = []
  }

  return { items, added, active, pending, waiting, add, cancel, clearFinished, reset, restore, resume }
})
