import { getJson, sendJson } from './http'

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
  /** Identifiants de tous les documents correspondant aux filtres (sélection « tout », UI-06). */
  all: number[]
  results: DocumentSummary[]
}

export const listDocuments = (params: URLSearchParams) =>
  getJson<DocumentPage>(`/api/documents?${params.toString()}`)

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

export const previewUrl = (id: number) => `${documentPath(id)}/preview`

export const downloadUrl = (id: number, original = false) =>
  `${documentPath(id)}/download${original ? '?original=true' : ''}`

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
