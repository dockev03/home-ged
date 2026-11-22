import type { UploadMetadata } from '../api/documents'
import { offlineDb, type Keyed } from './db'

/**
 * Envois en attente gardés sur l'appareil (PWA-10) : ils survivent à la fermeture de l'application
 * et partent au retour du réseau. Retirés dès que Paperless a reçu le fichier, ou à l'annulation.
 */
interface QueuedUploadRecord extends Keyed {
  userId: number
  name: string
  type: string
  lastModified: number
  blob: Blob
  metadata: UploadMetadata
  /** Ordre d'envoi (ms depuis l'époque Unix). */
  addedAt: number
}

export interface QueuedUpload {
  key: string
  file: File
  metadata: UploadMetadata
}

/** Garde un fichier à envoyer ; faux si le stockage l'a refusé (quota, navigation privée). */
export async function saveQueuedUpload(userId: number, upload: QueuedUpload): Promise<boolean> {
  try {
    await offlineDb.put('envois', {
      key: upload.key,
      userId,
      name: upload.file.name,
      type: upload.file.type,
      lastModified: upload.file.lastModified,
      blob: upload.file,
      // Copie simple : les métadonnées viennent parfois d'objets réactifs, que le stockage refuse.
      metadata: JSON.parse(JSON.stringify(upload.metadata)) as UploadMetadata,
      addedAt: Date.now(),
    } satisfies QueuedUploadRecord)
    return true
  } catch {
    return false
  }
}

export async function removeQueuedUpload(key: string) {
  try {
    await offlineDb.delete('envois', key)
  } catch {
    // Déjà absent ou stockage indisponible.
  }
}

/** Envois en attente de l'utilisateur, dans l'ordre d'ajout. */
export async function listQueuedUploads(userId: number): Promise<QueuedUpload[]> {
  try {
    const records = await offlineDb.getAll<QueuedUploadRecord>('envois')
    return records
      .filter((r) => r.userId === userId)
      .sort((a, b) => a.addedAt - b.addedAt)
      .map((r) => ({
        key: r.key,
        file: new File([r.blob], r.name, { type: r.type, lastModified: r.lastModified }),
        metadata: r.metadata,
      }))
  } catch {
    return []
  }
}
