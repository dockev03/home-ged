import { getJson } from './http'

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

export interface CustomField extends NamedItem {
  data_type: string
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
