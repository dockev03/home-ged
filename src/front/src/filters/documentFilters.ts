import type { LocationQuery, LocationQueryRaw } from 'vue-router'

/** Recherche texte : comme Paperless, sur le titre et le contenu, le titre seul, ou en plein texte. */
export type TextMode = 'titre_contenu' | 'titre' | 'avance'

/** Valeurs choisies dans une liste, ou « Aucun » (critère non renseigné), exclusif. */
export type Selection = number[] | 'aucun'

export type OwnerFilter = 'tous' | 'moi' | 'partages' | 'aucun'

export type SortField =
  | 'created'
  | 'added'
  | 'modified'
  | 'title'
  | 'correspondent__name'
  | 'document_type__name'
  | 'archive_serial_number'
  | 'num_notes'
  | 'page_count'
  | 'pertinence'

export const SORT_FIELDS: readonly { value: SortField; label: string }[] = [
  { value: 'created', label: 'Date du document' },
  { value: 'added', label: "Date d'ajout" },
  { value: 'modified', label: 'Date de modification' },
  { value: 'title', label: 'Titre' },
  { value: 'correspondent__name', label: 'Correspondant' },
  { value: 'document_type__name', label: 'Type de document' },
  { value: 'archive_serial_number', label: "Numéro d'archive" },
  { value: 'num_notes', label: 'Notes' },
  { value: 'page_count', label: 'Pages' },
  { value: 'pertinence', label: 'Pertinence (recherche avancée)' },
]

export const TEXT_MODES: readonly { value: TextMode; label: string }[] = [
  { value: 'titre_contenu', label: 'Titre et contenu' },
  { value: 'titre', label: 'Titre' },
  { value: 'avance', label: 'Recherche avancée' },
]

export interface DocumentFilters {
  texte: string
  mode: TextMode
  tags: Selection
  /** Tous les tags choisis (et) ou au moins un (ou). */
  tagsMode: 'tous' | 'un'
  correspondants: Selection
  types: Selection
  chemins: Selection
  /** Documents qui ont tous ces champs personnalisés. */
  champs: number[]
  /** Texte contenu dans la valeur d'un champ personnalisé. */
  champValeur: string
  creeDu: string
  creeAu: string
  ajouteDu: string
  ajouteAu: string
  proprietaire: OwnerFilter
  tri: SortField
  desc: boolean
}

export const defaultFilters = (): DocumentFilters => ({
  texte: '',
  mode: 'titre_contenu',
  tags: [],
  tagsMode: 'tous',
  correspondants: [],
  types: [],
  chemins: [],
  champs: [],
  champValeur: '',
  creeDu: '',
  creeAu: '',
  ajouteDu: '',
  ajouteAu: '',
  proprietaire: 'tous',
  tri: 'created',
  desc: true,
})

const DATE = /^\d{4}-\d{2}-\d{2}$/

function single(value: LocationQuery[string] | undefined): string {
  const first = Array.isArray(value) ? value[0] : value
  return typeof first === 'string' ? first.trim() : ''
}

function ids(value: string): number[] {
  return [
    ...new Set(
      value
        .split(',')
        .map((part) => Number(part))
        .filter((n) => Number.isSafeInteger(n) && n > 0),
    ),
  ]
}

function selection(value: string): Selection {
  return value === 'aucun' ? 'aucun' : ids(value)
}

function oneOf<T extends string>(value: string, choices: readonly T[], fallback: T): T {
  return choices.includes(value as T) ? (value as T) : fallback
}

function date(value: string): string {
  return DATE.test(value) ? value : ''
}

/** Lecture tolérante de l'URL : une valeur invalide est ignorée, jamais une erreur. */
export function fromQuery(query: LocationQuery): DocumentFilters {
  const get = (key: string) => single(query[key])
  const defaults = defaultFilters()
  return {
    texte: get('q'),
    mode: oneOf(get('mode'), TEXT_MODES.map((m) => m.value), defaults.mode),
    tags: selection(get('tags')),
    tagsMode: get('tags_mode') === 'un' ? 'un' : 'tous',
    correspondants: selection(get('correspondant')),
    types: selection(get('type')),
    chemins: selection(get('chemin')),
    champs: ids(get('champs')),
    champValeur: get('champ_valeur'),
    creeDu: date(get('cree_du')),
    creeAu: date(get('cree_au')),
    ajouteDu: date(get('ajoute_du')),
    ajouteAu: date(get('ajoute_au')),
    proprietaire: oneOf(get('proprio'), ['tous', 'moi', 'partages', 'aucun'] as const, defaults.proprietaire),
    tri: oneOf(get('tri'), SORT_FIELDS.map((s) => s.value), defaults.tri),
    desc: get('ordre') !== 'asc',
  }
}

const selectionToQuery = (value: Selection) => (value === 'aucun' ? 'aucun' : value.join(','))

/** Écriture dans l'URL : seuls les critères différents du défaut apparaissent. */
export function toQuery(filters: DocumentFilters): LocationQueryRaw {
  const query: Record<string, string> = {}
  const set = (key: string, value: string) => {
    if (value) query[key] = value
  }
  set('q', filters.texte.trim())
  if (filters.mode !== 'titre_contenu') set('mode', filters.mode)
  set('tags', selectionToQuery(filters.tags))
  if (filters.tagsMode === 'un' && Array.isArray(filters.tags) && filters.tags.length > 1) set('tags_mode', 'un')
  set('correspondant', selectionToQuery(filters.correspondants))
  set('type', selectionToQuery(filters.types))
  set('chemin', selectionToQuery(filters.chemins))
  set('champs', filters.champs.join(','))
  set('champ_valeur', filters.champValeur.trim())
  set('cree_du', filters.creeDu)
  set('cree_au', filters.creeAu)
  set('ajoute_du', filters.ajouteDu)
  set('ajoute_au', filters.ajouteAu)
  if (filters.proprietaire !== 'tous') set('proprio', filters.proprietaire)
  if (filters.tri !== 'created') set('tri', filters.tri)
  if (!filters.desc) set('ordre', 'asc')
  return query
}

/** Nombre de critères de filtre actifs (hors tri), pour le bouton « Réinitialiser ». */
export function activeCount(filters: DocumentFilters): number {
  const isSet = (value: Selection | string) => (Array.isArray(value) ? value.length > 0 : value !== '')
  return [
    filters.texte.trim(),
    filters.tags,
    filters.correspondants,
    filters.types,
    filters.chemins,
    filters.champs,
    filters.champValeur.trim(),
    filters.creeDu || filters.creeAu,
    filters.ajouteDu || filters.ajouteAu,
    filters.proprietaire === 'tous' ? '' : filters.proprietaire,
  ].filter((value) => isSet(value as Selection | string)).length
}

/** Paramètres de l'API de liste (noms Paperless, relayés tels quels par le BFF). */
export function toApiParams(
  filters: DocumentFilters,
  options: { page: number; pageSize: number; userId: number | null },
): URLSearchParams {
  const params = new URLSearchParams()
  const texte = filters.texte.trim()
  if (texte) {
    const key = { titre_contenu: 'title_content', titre: 'title__icontains', avance: 'query' }[filters.mode]
    params.set(key, texte)
  }

  if (filters.tags === 'aucun') params.set('is_tagged', '0')
  else if (filters.tags.length > 0)
    params.set(filters.tagsMode === 'un' ? 'tags__id__in' : 'tags__id__all', filters.tags.join(','))

  const relation = (value: Selection, field: string) => {
    if (value === 'aucun') params.set(`${field}__isnull`, '1')
    else if (value.length > 0) params.set(`${field}__id__in`, value.join(','))
  }
  relation(filters.correspondants, 'correspondent')
  relation(filters.types, 'document_type')
  relation(filters.chemins, 'storage_path')

  if (filters.champs.length > 0) params.set('custom_fields__id__all', filters.champs.join(','))
  if (filters.champValeur.trim()) params.set('custom_fields__icontains', filters.champValeur.trim())

  if (filters.creeDu) params.set('created__gte', filters.creeDu)
  if (filters.creeAu) params.set('created__lte', filters.creeAu)
  if (filters.ajouteDu) params.set('added__date__gte', filters.ajouteDu)
  if (filters.ajouteAu) params.set('added__date__lte', filters.ajouteAu)

  if (options.userId !== null) {
    const me = String(options.userId)
    if (filters.proprietaire === 'moi') params.set('owner__id', me)
    if (filters.proprietaire === 'partages') {
      params.set('owner__id__none', me)
      params.set('owner__isnull', '0')
    }
  }
  if (filters.proprietaire === 'aucun') params.set('owner__isnull', '1')

  // Pertinence : ordre du moteur plein texte, seulement en recherche avancée.
  const relevance = filters.tri === 'pertinence' && filters.mode === 'avance' && texte !== ''
  if (!relevance) {
    const field = filters.tri === 'pertinence' ? 'created' : filters.tri
    params.set('ordering', (filters.desc ? '-' : '') + field)
  }

  params.set('page', String(options.page))
  params.set('page_size', String(options.pageSize))
  return params
}
