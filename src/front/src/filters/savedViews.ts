import type { LocationQueryRaw } from 'vue-router'
import type { FilterRule, SavedView } from '../api/savedViews'
import { defaultFilters, SORT_FIELDS, toQuery, type DocumentFilters, type Selection, type SortField } from './documentFilters'

/**
 * Conversion entre les filtres de Home-GED et les règles des vues sauvegardées de Paperless
 * (`SavedViewFilterRule.RULE_TYPES`, Paperless 3.5). Une vue créée dans Home-GED s'ouvre dans
 * l'interface Paperless et inversement, pour les critères que Home-GED sait afficher.
 */
export const RULE = {
  TITLE: 0,
  CORRESPONDENT: 3,
  DOCUMENT_TYPE: 4,
  HAS_TAGS_ALL: 6,
  HAS_ANY_TAG: 7,
  TITLE_CONTENT: 19,
  FULLTEXT_QUERY: 20,
  HAS_TAGS_ANY: 22,
  STORAGE_PATH: 25,
  HAS_CORRESPONDENT_ANY: 26,
  HAS_DOCUMENT_TYPE_ANY: 28,
  HAS_STORAGE_PATH_ANY: 30,
  OWNER: 32,
  OWNER_ISNULL: 34,
  OWNER_DOES_NOT_INCLUDE: 35,
  CUSTOM_FIELDS_TEXT: 36,
  HAS_CUSTOM_FIELDS_ALL: 38,
  CREATED_TO: 43,
  CREATED_FROM: 44,
  ADDED_TO: 45,
  ADDED_FROM: 46,
  SIMPLE_TITLE: 48,
  SIMPLE_TEXT: 49,
} as const

/** Tri « pertinence » de Home-GED : champ `score` de la recherche plein texte de Paperless. */
const SCORE = 'score'

const DATE = /^\d{4}-\d{2}-\d{2}$/

export interface ViewDefinition {
  filter_rules: FilterRule[]
  sort_field: string
  sort_reverse: boolean
}

/** Règles et tri d'une vue à partir des filtres affichés. */
export function viewFromFilters(filters: DocumentFilters, userId: number | null): ViewDefinition {
  const rules: FilterRule[] = []
  const add = (rule_type: number, value: string | null) => rules.push({ rule_type, value })

  const texte = filters.texte.trim()
  if (texte) {
    const type = { titre_contenu: RULE.TITLE_CONTENT, titre: RULE.TITLE, avance: RULE.FULLTEXT_QUERY }[filters.mode]
    add(type, texte)
  }

  if (filters.tags === 'aucun') add(RULE.HAS_ANY_TAG, 'false')
  else for (const id of filters.tags) add(filters.tagsMode === 'un' ? RULE.HAS_TAGS_ANY : RULE.HAS_TAGS_ALL, String(id))

  const relation = (value: Selection, isRule: number, anyRule: number) => {
    if (value === 'aucun') add(isRule, null)
    else for (const id of value) add(anyRule, String(id))
  }
  relation(filters.correspondants, RULE.CORRESPONDENT, RULE.HAS_CORRESPONDENT_ANY)
  relation(filters.types, RULE.DOCUMENT_TYPE, RULE.HAS_DOCUMENT_TYPE_ANY)
  relation(filters.chemins, RULE.STORAGE_PATH, RULE.HAS_STORAGE_PATH_ANY)

  for (const id of filters.champs) add(RULE.HAS_CUSTOM_FIELDS_ALL, String(id))
  if (filters.champValeur.trim()) add(RULE.CUSTOM_FIELDS_TEXT, filters.champValeur.trim())

  if (filters.creeDu) add(RULE.CREATED_FROM, filters.creeDu)
  if (filters.creeAu) add(RULE.CREATED_TO, filters.creeAu)
  if (filters.ajouteDu) add(RULE.ADDED_FROM, filters.ajouteDu)
  if (filters.ajouteAu) add(RULE.ADDED_TO, filters.ajouteAu)

  if (userId !== null && filters.proprietaire === 'moi') add(RULE.OWNER, String(userId))
  if (userId !== null && filters.proprietaire === 'partages') {
    add(RULE.OWNER_DOES_NOT_INCLUDE, String(userId))
    add(RULE.OWNER_ISNULL, 'false')
  }
  if (filters.proprietaire === 'aucun') add(RULE.OWNER_ISNULL, 'true')

  return {
    filter_rules: rules,
    sort_field: filters.tri === 'pertinence' ? SCORE : filters.tri,
    sort_reverse: filters.desc,
  }
}

export interface ViewFilters {
  filters: DocumentFilters
  /** Règles que Home-GED ne sait pas afficher : ignorées dans la liste, conservées à l'enregistrement. */
  unsupported: FilterRule[]
}

const id = (value: string | null) => {
  const n = Number(value)
  return value !== null && /^\d+$/.test(value) && Number.isSafeInteger(n) && n > 0 ? n : null
}

/** Filtres affichés pour une vue ; les règles inconnues ou incompatibles sont mises à part. */
export function filtersFromView(view: Pick<SavedView, 'filter_rules' | 'sort_field' | 'sort_reverse'>, userId: number | null): ViewFilters {
  const filters = defaultFilters()
  const unsupported: FilterRule[] = []
  let tagsMode: 'tous' | 'un' | null = null
  let textSet = false
  const owner = { me: false, notMine: false, owned: false, unowned: false }
  const ownerRules: FilterRule[] = []

  const addTo = (current: Selection, value: number): Selection | null =>
    current === 'aucun' ? null : current.includes(value) ? current : [...current, value]

  for (const rule of view.filter_rules) {
    const value = rule.value
    let ok: boolean
    switch (rule.rule_type) {
      case RULE.TITLE:
      case RULE.SIMPLE_TITLE:
      case RULE.TITLE_CONTENT:
      case RULE.SIMPLE_TEXT:
      case RULE.FULLTEXT_QUERY: {
        ok = !textSet && !!value?.trim()
        if (ok) {
          textSet = true
          filters.texte = value!.trim()
          filters.mode =
            rule.rule_type === RULE.FULLTEXT_QUERY
              ? 'avance'
              : rule.rule_type === RULE.TITLE || rule.rule_type === RULE.SIMPLE_TITLE
                ? 'titre'
                : 'titre_contenu'
        }
        break
      }
      case RULE.HAS_TAGS_ALL:
      case RULE.HAS_TAGS_ANY: {
        const mode = rule.rule_type === RULE.HAS_TAGS_ALL ? 'tous' : 'un'
        const tag = id(value)
        const next = tag === null || (tagsMode !== null && tagsMode !== mode) ? null : addTo(filters.tags, tag)
        ok = next !== null
        if (next !== null) {
          filters.tags = next
          tagsMode = mode
        }
        break
      }
      case RULE.HAS_ANY_TAG:
        ok = (value === 'false' || value === '0') && Array.isArray(filters.tags) && filters.tags.length === 0
        if (ok) filters.tags = 'aucun'
        break
      case RULE.CORRESPONDENT:
      case RULE.DOCUMENT_TYPE:
      case RULE.STORAGE_PATH:
      case RULE.HAS_CORRESPONDENT_ANY:
      case RULE.HAS_DOCUMENT_TYPE_ANY:
      case RULE.HAS_STORAGE_PATH_ANY: {
        const key = (
          {
            [RULE.CORRESPONDENT]: 'correspondants',
            [RULE.HAS_CORRESPONDENT_ANY]: 'correspondants',
            [RULE.DOCUMENT_TYPE]: 'types',
            [RULE.HAS_DOCUMENT_TYPE_ANY]: 'types',
            [RULE.STORAGE_PATH]: 'chemins',
            [RULE.HAS_STORAGE_PATH_ANY]: 'chemins',
          } as const
        )[rule.rule_type]
        const isRule = rule.rule_type === RULE.CORRESPONDENT || rule.rule_type === RULE.DOCUMENT_TYPE || rule.rule_type === RULE.STORAGE_PATH
        if (isRule && value === null) {
          ok = Array.isArray(filters[key]) && filters[key].length === 0
          if (ok) filters[key] = 'aucun'
        } else {
          const ref = id(value)
          const next = ref === null ? null : addTo(filters[key], ref)
          ok = next !== null
          if (next !== null) filters[key] = next
        }
        break
      }
      case RULE.HAS_CUSTOM_FIELDS_ALL: {
        const field = id(value)
        ok = field !== null
        if (field !== null && !filters.champs.includes(field)) filters.champs = [...filters.champs, field]
        break
      }
      case RULE.CUSTOM_FIELDS_TEXT:
        ok = !filters.champValeur && !!value?.trim()
        if (ok) filters.champValeur = value!.trim()
        break
      case RULE.CREATED_FROM:
      case RULE.CREATED_TO:
      case RULE.ADDED_FROM:
      case RULE.ADDED_TO: {
        const key = (
          {
            [RULE.CREATED_FROM]: 'creeDu',
            [RULE.CREATED_TO]: 'creeAu',
            [RULE.ADDED_FROM]: 'ajouteDu',
            [RULE.ADDED_TO]: 'ajouteAu',
          } as const
        )[rule.rule_type]
        ok = value !== null && DATE.test(value) && !filters[key]
        if (ok) filters[key] = value!
        break
      }
      case RULE.OWNER:
        if (userId !== null && value === String(userId)) owner.me = true
        else unsupported.push(rule)
        ownerRules.push(rule)
        continue
      case RULE.OWNER_DOES_NOT_INCLUDE:
        if (userId !== null && value === String(userId)) owner.notMine = true
        else unsupported.push(rule)
        ownerRules.push(rule)
        continue
      case RULE.OWNER_ISNULL:
        if (value === 'true' || value === '1') owner.unowned = true
        else if (value === 'false' || value === '0') owner.owned = true
        else unsupported.push(rule)
        ownerRules.push(rule)
        continue
      default:
        ok = false
    }
    if (!ok) unsupported.push(rule)
  }

  if (tagsMode === 'un') filters.tagsMode = 'un'

  // Propriétaire : seules les combinaisons proposées par Home-GED sont reprises.
  const flags = [owner.me, owner.notMine, owner.owned, owner.unowned].filter(Boolean).length
  if (owner.me && flags === 1) filters.proprietaire = 'moi'
  else if (owner.notMine && owner.owned && flags === 2) filters.proprietaire = 'partages'
  else if (owner.unowned && flags === 1) filters.proprietaire = 'aucun'
  else for (const rule of ownerRules) if (!unsupported.includes(rule)) unsupported.push(rule)

  if (view.sort_field === SCORE) filters.tri = 'pertinence'
  else if (SORT_FIELDS.some((s) => s.value === view.sort_field)) filters.tri = view.sort_field as SortField
  filters.desc = view.sort_field ? view.sort_reverse : true

  return { filters, unsupported }
}

/** Adresse de la liste des documents filtrée par la vue. */
export function viewQuery(view: SavedView, userId: number | null): LocationQueryRaw {
  return { ...toQuery(filtersFromView(view, userId).filters), vue: String(view.id) }
}

/** Les filtres affichés diffèrent-ils de ceux de la vue ? */
export function differsFromView(filters: DocumentFilters, view: SavedView, userId: number | null): boolean {
  return JSON.stringify(toQuery(filters)) !== JSON.stringify(toQuery(filtersFromView(view, userId).filters))
}
