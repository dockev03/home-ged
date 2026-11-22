import { getJson, sendJson } from './http'

/** Règle de filtre Paperless : type numérique et valeur texte (null pour « non renseigné »). */
export interface FilterRule {
  rule_type: number
  value: string | null
}

/** Vue sauvegardée (UI-08), stockée dans Paperless. */
export interface SavedView {
  id: number
  name: string
  filter_rules: FilterRule[]
  sort_field: string | null
  sort_reverse: boolean
  owner: number | null
  /** Faux si la vue est partagée en lecture seule. */
  user_can_change?: boolean
}

export interface SavedViewList {
  vues: SavedView[]
  /** Vues affichées sur le tableau de bord, dans l'ordre. */
  tableau_de_bord: number[]
  /** Vues affichées dans le menu, dans l'ordre. */
  menu: number[]
}

/** Création ou modification ; l'affichage est une préférence de l'utilisateur. */
export interface SavedViewUpdate {
  name?: string
  filter_rules?: FilterRule[]
  sort_field?: string | null
  sort_reverse?: boolean
  tableau_de_bord?: boolean
  menu?: boolean
}

export const listSavedViews = () => getJson<SavedViewList>('/api/vues')

export const createSavedView = (body: SavedViewUpdate) => sendJson<SavedView>('POST', '/api/vues', body)

export const updateSavedView = (id: number, body: SavedViewUpdate) => sendJson<SavedView>('PATCH', `/api/vues/${id}`, body)

export const deleteSavedView = (id: number) => sendJson('DELETE', `/api/vues/${id}`)

/** Statistiques de Paperless, limitées aux documents visibles par l'utilisateur. */
export interface Statistics {
  documents_total: number
  documents_inbox: number | null
  inbox_tags: number[] | null
  tag_count: number
  correspondent_count: number
  document_type_count: number
  storage_path_count: number
  character_count: number
  document_file_type_counts: { mime_type: string; mime_type_count: number }[]
}

export const getStatistics = () => getJson<Statistics>('/api/tableau-de-bord/statistiques')

export interface Deadline {
  id: number
  title: string
  correspondent: number | null
  tags: number[]
  /** Date d'échéance (AAAA-MM-JJ). */
  echeance: string | null
}

export interface Deadlines {
  /** Champ « Échéance » de Paperless ; null s'il n'existe pas. */
  champ: { id: number; name: string } | null
  count: number
  results: Deadline[]
}

export const getDeadlines = (nombre: number) => getJson<Deadlines>(`/api/tableau-de-bord/echeances?nombre=${nombre}`)
