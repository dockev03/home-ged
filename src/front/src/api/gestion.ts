import type { CustomFieldType, DocumentPermissions } from './documents'
import { getJson, sendJson } from './http'

/** Listes gérables (UI-07), nommées comme côté BFF. */
export type ManagedKind = 'tags' | 'correspondants' | 'types' | 'chemins' | 'champs'

/** Élément d'une liste « Gérer » ; les champs présents dépendent de la liste. */
export interface ManagedItem {
  id: number
  name: string
  document_count?: number
  /** Étiquettes, correspondants, types et chemins : classement automatique. */
  match?: string
  matching_algorithm?: number
  is_insensitive?: boolean
  owner?: number | null
  permissions?: DocumentPermissions['permissions']
  user_can_change?: boolean
  /** Étiquettes. */
  color?: string
  text_color?: string
  is_inbox_tag?: boolean
  /** Correspondants : date du dernier document. */
  last_correspondence?: string | null
  /** Chemins de stockage : modèle de chemin. */
  path?: string
  /** Champs personnalisés. */
  data_type?: CustomFieldType
  extra_data?: {
    select_options?: { id?: string; label: string }[]
    default_currency?: string | null
  } | null
}

export interface ManagedPage {
  count: number
  results: ManagedItem[]
}

export interface ManagedQuery {
  page: number
  pageSize: number
  /** Recherche dans le nom. */
  nom: string
  /** Champ de tri, préfixé par « - » pour l'ordre décroissant. */
  tri: string
}

/** Corps de création ou de modification ; seuls les champs modifiés sont envoyés. */
export type ManagedUpdate = Partial<
  Omit<ManagedItem, 'id' | 'document_count' | 'permissions' | 'user_can_change' | 'text_color' | 'last_correspondence'>
> & { set_permissions?: DocumentPermissions['permissions'] }

const base = (kind: ManagedKind) => `/api/gestion/${kind}`

export function listManaged(kind: ManagedKind, query: ManagedQuery) {
  const params = new URLSearchParams({ page: String(query.page), page_size: String(query.pageSize), tri: query.tri })
  if (query.nom.trim()) params.set('nom', query.nom.trim())
  return getJson<ManagedPage>(`${base(kind)}?${params.toString()}`)
}

export const createManaged = (kind: ManagedKind, body: ManagedUpdate) =>
  sendJson<ManagedItem>('POST', base(kind), body)

export const updateManaged = (kind: ManagedKind, id: number, body: ManagedUpdate) =>
  sendJson<ManagedItem>('PATCH', `${base(kind)}/${id}`, body)

export const deleteManaged = (kind: ManagedKind, id: number) => sendJson('DELETE', `${base(kind)}/${id}`)
