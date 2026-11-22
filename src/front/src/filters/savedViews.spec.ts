import { describe, expect, it } from 'vitest'
import type { SavedView } from '../api/savedViews'
import { defaultFilters, type DocumentFilters } from './documentFilters'
import { differsFromView, filtersFromView, RULE, viewFromFilters, viewQuery } from './savedViews'

const ME = 7

const view = (overrides: Partial<SavedView> = {}): SavedView => ({
  id: 3,
  name: 'Factures',
  filter_rules: [],
  sort_field: 'created',
  sort_reverse: true,
  owner: ME,
  user_can_change: true,
  ...overrides,
})

const filters = (overrides: Partial<DocumentFilters>): DocumentFilters => ({ ...defaultFilters(), ...overrides })

describe('viewFromFilters', () => {
  it('traduit chaque critère en règle Paperless', () => {
    const result = viewFromFilters(
      filters({
        texte: ' facture ',
        tags: [1, 2],
        correspondants: 'aucun',
        types: [4],
        chemins: [5, 6],
        champs: [8],
        champValeur: 'EDF',
        creeDu: '2026-01-01',
        creeAu: '2026-12-31',
        ajouteDu: '2026-02-01',
        ajouteAu: '2026-02-28',
        proprietaire: 'moi',
        tri: 'title',
        desc: false,
      }),
      ME,
    )

    expect(result.filter_rules).toEqual([
      { rule_type: RULE.TITLE_CONTENT, value: 'facture' },
      { rule_type: RULE.HAS_TAGS_ALL, value: '1' },
      { rule_type: RULE.HAS_TAGS_ALL, value: '2' },
      { rule_type: RULE.CORRESPONDENT, value: null },
      { rule_type: RULE.HAS_DOCUMENT_TYPE_ANY, value: '4' },
      { rule_type: RULE.HAS_STORAGE_PATH_ANY, value: '5' },
      { rule_type: RULE.HAS_STORAGE_PATH_ANY, value: '6' },
      { rule_type: RULE.HAS_CUSTOM_FIELDS_ALL, value: '8' },
      { rule_type: RULE.CUSTOM_FIELDS_TEXT, value: 'EDF' },
      { rule_type: RULE.CREATED_FROM, value: '2026-01-01' },
      { rule_type: RULE.CREATED_TO, value: '2026-12-31' },
      { rule_type: RULE.ADDED_FROM, value: '2026-02-01' },
      { rule_type: RULE.ADDED_TO, value: '2026-02-28' },
      { rule_type: RULE.OWNER, value: '7' },
    ])
    expect(result.sort_field).toBe('title')
    expect(result.sort_reverse).toBe(false)
  })

  it('choisit la règle selon le mode de recherche et d’étiquettes', () => {
    expect(viewFromFilters(filters({ texte: 'a', mode: 'titre' }), ME).filter_rules[0]!.rule_type).toBe(RULE.TITLE)
    expect(viewFromFilters(filters({ texte: 'a', mode: 'avance', tri: 'pertinence' }), ME)).toMatchObject({
      filter_rules: [{ rule_type: RULE.FULLTEXT_QUERY, value: 'a' }],
      sort_field: 'score',
    })
    expect(viewFromFilters(filters({ tags: [1], tagsMode: 'un' }), ME).filter_rules).toEqual([
      { rule_type: RULE.HAS_TAGS_ANY, value: '1' },
    ])
    expect(viewFromFilters(filters({ tags: 'aucun' }), ME).filter_rules).toEqual([{ rule_type: RULE.HAS_ANY_TAG, value: 'false' }])
  })

  it('traduit les propriétaires « partagés avec moi » et « sans propriétaire »', () => {
    expect(viewFromFilters(filters({ proprietaire: 'partages' }), ME).filter_rules).toEqual([
      { rule_type: RULE.OWNER_DOES_NOT_INCLUDE, value: '7' },
      { rule_type: RULE.OWNER_ISNULL, value: 'false' },
    ])
    expect(viewFromFilters(filters({ proprietaire: 'aucun' }), ME).filter_rules).toEqual([
      { rule_type: RULE.OWNER_ISNULL, value: 'true' },
    ])
  })
})

describe('filtersFromView', () => {
  it('relit les filtres enregistrés par Home-GED', () => {
    const original = filters({
      texte: 'impôts',
      mode: 'avance',
      tags: [3, 1],
      tagsMode: 'un',
      correspondants: [2],
      types: 'aucun',
      chemins: [],
      champs: [4, 9],
      creeDu: '2025-01-01',
      ajouteAu: '2026-03-01',
      proprietaire: 'partages',
      tri: 'pertinence',
      desc: true,
    })

    const result = filtersFromView(view(viewFromFilters(original, ME)), ME)

    expect(result.unsupported).toEqual([])
    expect(result.filters).toEqual(original)
  })

  it('reprend les règles créées par l’interface Paperless', () => {
    const result = filtersFromView(
      view({
        filter_rules: [
          { rule_type: RULE.SIMPLE_TEXT, value: 'assurance' },
          { rule_type: RULE.CORRESPONDENT, value: '5' },
          { rule_type: RULE.HAS_CORRESPONDENT_ANY, value: '6' },
          { rule_type: RULE.HAS_TAGS_ALL, value: '2' },
          { rule_type: RULE.OWNER_ISNULL, value: 'true' },
        ],
        sort_field: 'added',
        sort_reverse: false,
      }),
      ME,
    )

    expect(result.unsupported).toEqual([])
    expect(result.filters).toMatchObject({
      texte: 'assurance',
      mode: 'titre_contenu',
      correspondants: [5, 6],
      tags: [2],
      tagsMode: 'tous',
      proprietaire: 'aucun',
      tri: 'added',
      desc: false,
    })
  })

  it('met à part les règles que Home-GED ne sait pas afficher', () => {
    const rules = [
      { rule_type: 21, value: '12' }, // plus comme celui-ci
      { rule_type: 27, value: '3' }, // correspondant exclu
      { rule_type: RULE.HAS_TAGS_ALL, value: '1' },
      { rule_type: RULE.HAS_TAGS_ANY, value: '2' }, // mode d'étiquettes mélangé
      { rule_type: RULE.TITLE, value: 'a' },
      { rule_type: RULE.TITLE_CONTENT, value: 'b' }, // deuxième recherche texte
      { rule_type: RULE.CREATED_FROM, value: 'hier' },
      { rule_type: RULE.OWNER, value: '99' }, // autre propriétaire
    ]

    const result = filtersFromView(view({ filter_rules: rules }), ME)

    expect(result.unsupported).toEqual([rules[0], rules[1], rules[3], rules[5], rules[6], rules[7]])
    expect(result.filters).toMatchObject({ tags: [1], texte: 'a', mode: 'titre', proprietaire: 'tous', creeDu: '' })
  })

  it('n’accepte que les combinaisons de propriétaire proposées par Home-GED', () => {
    const hideUnowned = { rule_type: RULE.OWNER_ISNULL, value: 'false' }
    expect(filtersFromView(view({ filter_rules: [hideUnowned] }), ME).unsupported).toEqual([hideUnowned])

    const mine = { rule_type: RULE.OWNER, value: '7' }
    expect(filtersFromView(view({ filter_rules: [mine] }), null).unsupported).toEqual([mine])
  })

  it('trie par date du document, du plus récent au plus ancien, sans tri connu', () => {
    expect(filtersFromView(view({ sort_field: null, sort_reverse: false }), ME).filters).toMatchObject({ tri: 'created', desc: true })
    expect(filtersFromView(view({ sort_field: 'custom_field_3', sort_reverse: false }), ME).filters.tri).toBe('created')
  })
})

describe('viewQuery et differsFromView', () => {
  const saved = view({ id: 12, filter_rules: [{ rule_type: RULE.HAS_TAGS_ALL, value: '4' }], sort_field: 'title', sort_reverse: false })

  it('ouvre la liste filtrée en repérant la vue', () => {
    expect(viewQuery(saved, ME)).toEqual({ tags: '4', tri: 'title', ordre: 'asc', vue: '12' })
  })

  it('signale des filtres modifiés depuis l’ouverture de la vue', () => {
    const { filters: opened } = filtersFromView(saved, ME)
    expect(differsFromView(opened, saved, ME)).toBe(false)
    expect(differsFromView({ ...opened, tags: [4, 5] }, saved, ME)).toBe(true)
    expect(differsFromView({ ...opened, desc: true }, saved, ME)).toBe(true)
  })
})
