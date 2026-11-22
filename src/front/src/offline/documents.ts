import { previewUrl, thumbnailUrl, type DocumentDetail } from '../api/documents'
import { hasOfflineStorage, offlineDb, type Keyed } from './db'

/**
 * Documents consultés récemment, lisibles hors ligne (PWA-09) : la fiche (détails, contenu, notes),
 * l'aperçu et la vignette. Les plus anciens sortent au-delà de 20 documents ou de 150 Mo.
 * Tout est effacé à la déconnexion et à l'expiration de la session (voir session.ts).
 */
export const MAX_OFFLINE_DOCUMENTS = 20
export const MAX_OFFLINE_BYTES = 150 * 1024 * 1024
/** Un aperçu plus gros n'est pas gardé : il pousserait dehors tous les autres documents. */
export const MAX_OFFLINE_FILE_BYTES = 50 * 1024 * 1024

export type OfflineFileKind = 'apercu' | 'vignette'

interface DocumentRecord extends Keyed {
  userId: number
  document: DocumentDetail
  /** Dernière consultation (ms depuis l'époque Unix) : ordre de sortie du cache. */
  openedAt: number
  /** Enregistrement de la copie (ISO 8601). */
  savedAt: string
  /** Taille de la fiche (JSON) et des fichiers gardés, en octets. */
  size: number
  /** Fichier du document pour lequel l'aperçu et la vignette ont été téléchargés (ou écartés). */
  filesSignature: string | null
  files: OfflineFileKind[]
}

interface FileRecord extends Keyed {
  blob: Blob
}

export interface OfflineDocument {
  document: DocumentDetail
  savedAt: string
  files: OfflineFileKind[]
}

const documentKey = (id: number) => String(id)
const fileKey = (id: number, kind: OfflineFileKind) => `${id}:${kind}`

/** Identifie le fichier affiché : un changement de version ou d'archive impose de retélécharger l'aperçu. */
export function filesSignature(document: DocumentDetail): string {
  const current = document.versions?.[0]
  return [current?.id ?? '', current?.checksum ?? '', document.archived_file_name ?? '', document.mime_type].join('|')
}

/**
 * Documents à retirer : les plus récemment consultés sont gardés tant que le nombre et la taille
 * totale restent dans les limites.
 */
export function toEvict(
  entries: { key: string; openedAt: number; size: number }[],
  limits = { count: MAX_OFFLINE_DOCUMENTS, bytes: MAX_OFFLINE_BYTES },
): string[] {
  const sorted = [...entries].sort((a, b) => b.openedAt - a.openedAt)
  const evicted: string[] = []
  let kept = 0
  let bytes = 0
  for (const entry of sorted) {
    if (kept < limits.count && bytes + entry.size <= limits.bytes) {
      kept++
      bytes += entry.size
    } else evicted.push(entry.key)
  }
  return evicted
}

async function removeDocument(key: string) {
  await Promise.allSettled([
    offlineDb.delete('documents', key),
    offlineDb.delete('fichiers', `${key}:apercu`),
    offlineDb.delete('fichiers', `${key}:vignette`),
  ])
}

async function evict() {
  const records = await offlineDb.getAll<DocumentRecord>('documents')
  for (const key of toEvict(records)) await removeDocument(key)
}

/**
 * Télécharge un fichier du document : le contenu, « ignore » s'il est trop gros ou introuvable (inutile
 * de réessayer), « echec » si le réseau ou le serveur a manqué (à réessayer à la prochaine consultation).
 */
async function download(url: string): Promise<Blob | 'ignore' | 'echec'> {
  try {
    const response = await fetch(url, { credentials: 'same-origin' })
    if (response.status === 404) return 'ignore'
    if (!response.ok) return 'echec'
    const length = Number(response.headers.get('Content-Length') ?? 0)
    if (length > MAX_OFFLINE_FILE_BYTES) return 'ignore'
    const blob = await response.blob()
    return blob.size > MAX_OFFLINE_FILE_BYTES ? 'ignore' : blob
  } catch {
    return 'echec'
  }
}

const jsonSize = (document: DocumentDetail) => new Blob([JSON.stringify(document)]).size

/**
 * Fiche consultée en ligne : elle est gardée (ou rafraîchie) pour la lecture hors ligne, avec son
 * aperçu et sa vignette, téléchargés seulement si le fichier du document a changé. Sans effet en
 * cas d'erreur : le hors-ligne est un plus, jamais un obstacle.
 */
export async function rememberDocument(userId: number, document: DocumentDetail) {
  if (!hasOfflineStorage()) return
  try {
    const key = documentKey(document.id)
    const previous = await offlineDb.get<DocumentRecord>('documents', key)
    const signature = filesSignature(document)
    const sameFiles = previous?.userId === userId && previous.filesSignature === signature
    let files: OfflineFileKind[] = sameFiles ? previous.files : []
    let filesSize = sameFiles ? previous.size - jsonSize(previous.document) : 0
    let downloaded = sameFiles

    const record = (): DocumentRecord => ({
      key,
      userId,
      document,
      openedAt: Date.now(),
      savedAt: new Date().toISOString(),
      size: jsonSize(document) + filesSize,
      filesSignature: downloaded ? signature : null,
      files,
    })
    await offlineDb.put('documents', record())
    if (sameFiles) return void (await evict())

    files = []
    filesSize = 0
    downloaded = true
    for (const [kind, url] of [
      ['vignette', thumbnailUrl(document.id)],
      ['apercu', previewUrl(document.id)],
    ] as const) {
      const blob = await download(url)
      if (blob === 'echec') downloaded = false
      if (typeof blob === 'string') continue
      await offlineDb.put('fichiers', { key: fileKey(document.id, kind), blob } satisfies FileRecord)
      files.push(kind)
      filesSize += blob.size
    }
    // Le document a pu être retiré entre-temps (suppression, déconnexion) : on ne le recrée pas.
    if (!(await offlineDb.get<DocumentRecord>('documents', key))) {
      await removeDocument(key)
      return
    }
    await offlineDb.put('documents', record())
    await evict()
  } catch {
    // Stockage plein ou indisponible : le document ne sera simplement pas lisible hors ligne.
  }
}

/** Copie hors ligne d'un document de l'utilisateur ; null s'il n'a pas été consulté récemment. */
export async function getOfflineDocument(userId: number, id: number): Promise<OfflineDocument | null> {
  try {
    const record = await offlineDb.get<DocumentRecord>('documents', documentKey(id))
    if (!record || record.userId !== userId) return null
    return { document: record.document, savedAt: record.savedAt, files: record.files }
  } catch {
    return null
  }
}

export async function getOfflineFile(id: number, kind: OfflineFileKind): Promise<Blob | null> {
  try {
    return (await offlineDb.get<FileRecord>('fichiers', fileKey(id, kind)))?.blob ?? null
  } catch {
    return null
  }
}

/** Documents lisibles hors ligne, le plus récemment consulté d'abord. */
export async function listOfflineDocuments(userId: number): Promise<OfflineDocument[]> {
  try {
    const records = await offlineDb.getAll<DocumentRecord>('documents')
    return records
      .filter((r) => r.userId === userId)
      .sort((a, b) => b.openedAt - a.openedAt)
      .map((r) => ({ document: r.document, savedAt: r.savedAt, files: r.files }))
  } catch {
    return []
  }
}

/** Document supprimé : sa copie hors ligne disparaît aussi. */
export async function forgetDocument(id: number) {
  await removeDocument(documentKey(id))
}
