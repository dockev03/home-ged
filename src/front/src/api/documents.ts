import { ANTI_FORGERY_HEADER, ApiError, getJson, postForFile, sendJson } from './http'

/** Document tel que renvoyé par la liste Paperless (champs utilisés par Home-GED). */
export interface DocumentSummary {
  id: number
  title: string
  /** Extrait du contenu OCR (tronqué par le BFF). */
  content?: string
  tags: number[]
  correspondent: number | null
  document_type: number | null
  storage_path: number | null
  /** Date du document (AAAA-MM-JJ). */
  created: string
  /** Date d'ajout (ISO 8601). */
  added: string
  archive_serial_number: number | null
  page_count?: number | null
  notes?: unknown[]
  /** Présent en recherche plein texte : extrait surligné par Paperless. */
  __search_hit__?: { score: number; highlights: string; note_highlights?: string; rank: number }
}

export interface DocumentPage {
  count: number
  results: DocumentSummary[]
}

export const listDocuments = (params: URLSearchParams) =>
  getJson<DocumentPage>(`/api/documents?${params.toString()}`)

/** Identifiants de tous les documents filtrés, dans l'ordre de la liste (précédent / suivant, sélection « tout »). */
export const listDocumentIds = (params: URLSearchParams) =>
  getJson<number[]>(`/api/documents/ids?${params.toString()}`)

export const thumbnailUrl = (id: number) => `/api/documents/${id}/thumb`

export interface Tag {
  id: number
  name: string
  color: string
  text_color: string
  is_inbox_tag: boolean
}

export interface NamedItem {
  id: number
  name: string
}

export type CustomFieldType =
  | 'string'
  | 'longtext'
  | 'url'
  | 'date'
  | 'boolean'
  | 'integer'
  | 'float'
  | 'monetary'
  | 'select'
  | 'documentlink'

export interface CustomField extends NamedItem {
  data_type: CustomFieldType
  extra_data?: {
    select_options?: { id: string; label: string }[]
    default_currency?: string | null
  } | null
}

export interface References {
  tags: Tag[]
  correspondants: NamedItem[]
  types: NamedItem[]
  chemins: NamedItem[]
  champs: CustomField[]
}

export type ReferenceKind = keyof References

export const getReferences = <K extends ReferenceKind>(kind: K) =>
  getJson<References[K]>(`/api/references/${kind}`)

/** Valeur d'un champ personnalisé sur un document. */
export interface CustomFieldValue {
  field: number
  value: unknown
}

export interface Note {
  id: number
  note: string
  created: string
  user?: { id: number; username: string; first_name?: string; last_name?: string } | null
}

/** Document complet (fiche). */
export interface DocumentDetail extends DocumentSummary {
  content: string
  modified: string
  custom_fields: CustomFieldValue[]
  notes: Note[]
  owner: number | null
  /** Faux si l'utilisateur n'a que le droit de lecture. */
  user_can_change?: boolean
  original_file_name: string | null
  archived_file_name: string | null
  mime_type: string
  /** Versions Paperless 3, la plus récente d'abord ; la version d'origine a is_root. */
  versions?: DocumentVersion[]
}

export interface DocumentVersion {
  id: number
  added: string
  version_label: string | null
  checksum: string | null
  is_root: boolean
}

/** Champs modifiables depuis la fiche (UI-05). */
export interface DocumentUpdate {
  title?: string
  archive_serial_number?: number | null
  created?: string
  correspondent?: number | null
  document_type?: number | null
  storage_path?: number | null
  tags?: number[]
  custom_fields?: CustomFieldValue[]
}

export interface DocumentMetadata {
  original_checksum: string
  original_size: number | null
  original_mime_type: string
  original_filename: string
  has_archive_version: boolean
  archive_checksum: string | null
  archive_size: number | null
  original_metadata: MetadataEntry[]
  archive_metadata: MetadataEntry[] | null
}

export interface MetadataEntry {
  namespace: string
  prefix: string
  key: string
  value: string
}

const documentPath = (id: number) => `/api/documents/${id}`

export const getDocument = (id: number) => getJson<DocumentDetail>(documentPath(id))

export const updateDocument = (id: number, update: DocumentUpdate) =>
  sendJson<DocumentDetail>('PATCH', documentPath(id), update)

/** Paperless place le document dans sa corbeille. */
export const deleteDocument = (id: number) => sendJson('DELETE', documentPath(id))

export const getMetadata = (id: number) => getJson<DocumentMetadata>(`${documentPath(id)}/metadata`)

export const getNotes = (id: number) => getJson<Note[]>(`${documentPath(id)}/notes`)

export const addNote = (id: number, note: string) => sendJson<Note[]>('POST', `${documentPath(id)}/notes`, { note })

export const deleteNote = (id: number, noteId: number) =>
  sendJson<Note[]>('DELETE', `${documentPath(id)}/notes/${noteId}`)

function fileQuery(original: boolean, version: number | null) {
  const params = new URLSearchParams()
  if (original) params.set('original', 'true')
  if (version !== null) params.set('version', String(version))
  const query = params.toString()
  return query ? `?${query}` : ''
}

export const previewUrl = (id: number, version: number | null = null) =>
  `${documentPath(id)}/preview${fileQuery(false, version)}`

export const downloadUrl = (id: number, original = false, version: number | null = null) =>
  `${documentPath(id)}/download${fileQuery(original, version)}`

/** Entrée du journal d'audit Paperless (django-auditlog). */
export interface HistoryEntry {
  id: number
  timestamp: string
  action: string
  changes: Record<string, unknown> | null
  actor: { id: number; username: string } | null
}

export const getHistory = (id: number) => getJson<HistoryEntry[]>(`${documentPath(id)}/history`)

export interface Grantees {
  users: number[]
  groups: number[]
}

export interface DocumentPermissions {
  owner: number | null
  permissions: { view: Grantees; change: Grantees }
}

export const getPermissions = (id: number) => getJson<DocumentPermissions>(`${documentPath(id)}/permissions`)

export const updatePermissions = (id: number, owner: number | null, permissions: DocumentPermissions['permissions']) =>
  sendJson<DocumentPermissions>('PATCH', `${documentPath(id)}/permissions`, { owner, set_permissions: permissions })

export interface PaperlessUser {
  id: number
  username: string
  first_name?: string
  last_name?: string
}

/** Comptes et groupes Paperless : lisibles seulement avec les droits « voir les utilisateurs / groupes ». */
export const listUsers = () => getJson<PaperlessUser[]>('/api/references/utilisateurs')

export const listGroups = () => getJson<NamedItem[]>('/api/references/groupes')

export const renameVersion = (id: number, versionId: number, label: string) =>
  sendJson('PATCH', `${documentPath(id)}/versions/${versionId}`, { versionLabel: label })

export const deleteVersion = (id: number, versionId: number) =>
  sendJson('DELETE', `${documentPath(id)}/versions/${versionId}`)

/** Taille maximale d'un fichier envoyé (alignée sur NPM, Home-GED et Paperless). */
export const MAX_UPLOAD_BYTES = 200 * 1024 * 1024

/**
 * Envoi d'un formulaire multipart au BFF. XMLHttpRequest plutôt que fetch : seul lui donne
 * l'avancement de l'envoi. Renvoie l'identifiant de la tâche de traitement Paperless.
 */
function sendFile(path: string, form: FormData, onProgress: (fraction: number) => void, signal?: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('POST', path)
    request.withCredentials = true
    request.setRequestHeader('Accept', 'application/json')
    for (const [name, value] of Object.entries(ANTI_FORGERY_HEADER)) request.setRequestHeader(name, value)
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(event.loaded / event.total)
    }
    request.onload = () => {
      let body: { tache?: string; title?: string; code?: string } = {}
      try {
        body = JSON.parse(request.responseText || '{}')
      } catch {
        // Corps non JSON : message générique.
      }
      if (request.status === 202 && body.tache) resolve(body.tache)
      else reject(new ApiError(request.status, body.title ?? `Envoi refusé (erreur ${request.status}).`, body.code ?? null))
    }
    request.onerror = () => reject(new TypeError('réseau'))
    request.onabort = () => reject(new DOMException('Envoi annulé.', 'AbortError'))
    signal?.addEventListener('abort', () => request.abort(), { once: true })
    request.send(form)
  })
}

/** Envoie une nouvelle version du document ; renvoie la tâche de traitement Paperless. */
export function uploadVersion(
  id: number,
  file: File,
  label: string,
  onProgress: (fraction: number) => void = () => {},
): Promise<string> {
  const form = new FormData()
  form.append('document', file, file.name)
  if (label.trim()) form.append('version_label', label.trim())
  return sendFile(`${documentPath(id)}/versions`, form, onProgress)
}

/** Ajoute un document (PWA-07) ; Paperless le consomme en tâche de fond (OCR, classement). */
export function uploadDocument(file: File, onProgress: (fraction: number) => void = () => {}, signal?: AbortSignal) {
  const form = new FormData()
  form.append('document', file, file.name)
  return sendFile('/api/documents/upload', form, onProgress, signal)
}

export type TaskStatus = 'pending' | 'started' | 'success' | 'failure' | 'revoked'

export interface TaskState {
  status: TaskStatus
  message: string | null
  document_id: number | null
}

export const getTask = (taskId: string) => getJson<TaskState>(`/api/taches/${taskId}`)

export type CreatableKind = Exclude<ReferenceKind, 'champs'>

export interface CreateReference {
  name: string
  /** Étiquettes : couleur #rrggbb. */
  color?: string
  /** Chemins de stockage : modèle de chemin Paperless. */
  path?: string
}

export const createReference = <K extends CreatableKind>(kind: K, body: CreateReference) =>
  sendJson<References[K][number]>('POST', `/api/references/${kind}`, body)

/** Action en masse (UI-06), au format bulk_edit de Paperless ; le BFF n'accepte que celles-ci. */
export type BulkAction =
  | { method: 'modify_tags'; parameters: { add_tags: number[]; remove_tags: number[] } }
  | { method: 'set_correspondent'; parameters: { correspondent: number | null } }
  | { method: 'set_document_type'; parameters: { document_type: number | null } }
  | { method: 'set_storage_path'; parameters: { storage_path: number | null } }
  | {
      method: 'modify_custom_fields'
      parameters: { add_custom_fields: CustomFieldValue[]; remove_custom_fields: number[] }
    }
  | {
      method: 'set_permissions'
      parameters: { owner: number | null; set_permissions: DocumentPermissions['permissions']; merge: boolean }
    }
  | { method: 'delete'; parameters: Record<string, never> }

/**
 * Applique l'action à tous les documents ; Paperless refuse l'ensemble si l'un n'est pas modifiable.
 * Renvoie la tâche de fond de Paperless quand il exécute l'action en différé (Paperless 3.5).
 */
export const bulkEdit = (documents: number[], action: BulkAction) =>
  sendJson<{ tache: string } | null>('POST', '/api/documents/actions', { documents, ...action })

/** Contenu de l'archive : PDF d'archive (OCR), fichiers d'origine, ou les deux. */
export type ArchiveContent = 'archive' | 'originaux' | 'les_deux'

export const bulkDownload = (documents: number[], contenu: ArchiveContent) =>
  postForFile('/api/documents/telechargement', { documents, contenu })
