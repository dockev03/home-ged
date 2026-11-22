import { offlineDb, type Keyed } from './db'

/**
 * Fichiers reçus d'une autre application par « Partager vers Home-GED » (PWA-11). Le service
 * worker les range sur l'appareil (`public/partage-sw.js`) ; l'écran « Documents partagés » les
 * confie ensuite à la file d'envoi ou les abandonne. Effacés à la déconnexion, comme le reste.
 */
export interface SharedFileRecord extends Keyed {
  name: string
  type: string
  lastModified: number
  blob: Blob
  /** Réception par le service worker (ms depuis l'époque Unix). */
  receivedAt: number
}

export interface SharedFile {
  key: string
  file: File
}

/** Fichiers reçus, dans l'ordre de réception ; vide si le stockage est indisponible. */
export async function listSharedFiles(): Promise<SharedFile[]> {
  try {
    const records = await offlineDb.getAll<SharedFileRecord>('partages')
    return records
      .sort((a, b) => a.receivedAt - b.receivedAt)
      .map((r) => ({ key: r.key, file: new File([r.blob], r.name, { type: r.type, lastModified: r.lastModified }) }))
  } catch {
    return []
  }
}

/** Retire des fichiers reçus (envoyés ou abandonnés). */
export async function removeSharedFiles(keys: readonly string[]) {
  await Promise.allSettled(keys.map((key) => offlineDb.delete('partages', key)))
}
