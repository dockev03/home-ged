import { describe, expect, it } from 'vitest'
import { activeCount, defaultFilters, fromQuery, toApiParams, toQuery, type DocumentFilters } from './documentFilters'

const api = (overrides: Partial<DocumentFilters>, userId: number | null = 7) =>
  Object.fromEntries(toApiParams({ ...defaultFilters(), ...overrides }, { page: 2, pageSize: 25, userId }))

describe('filtres de documents', () => {
  it('URL vide → filtres par défaut, et inversement', () => {
    expect(fromQuery({})).toEqual(defaultFilters())
    expect(toQuery(defaultFilters())).toEqual({})
    expect(activeCount(defaultFilters())).toBe(0)
  })

  it('aller-retour URL ↔ filtres', () => {
    const filters: DocumentFilters = {
      texte: 'facture edf',
      mode: 'avance',
      tags: [3, 1],
      tagsMode: 'un',
      correspondants: 'aucun',
      types: [4],
      chemins: [],
      champs: [2],
      champValeur: 'alice',
      creeDu: '2025-01-01',
      creeAu: '2025-12-31',
      ajouteDu: '',
      ajouteAu: '2026-11-01',
      proprietaire: 'partages',
      tri: 'title',
      desc: false,
    }
    const query = toQuery(filters)
    expect(query).toEqual({
      q: 'facture edf',
      mode: 'avance',
      tags: '3,1',
      tags_mode: 'un',
      correspondant: 'aucun',
      type: '4',
      champs: '2',
      champ_valeur: 'alice',
      cree_du: '2025-01-01',
      cree_au: '2025-12-31',
      ajoute_au: '2026-11-01',
      proprio: 'partages',
      tri: 'title',
      ordre: 'asc',
    })
    expect(fromQuery(query as Record<string, string>)).toEqual(filters)
    expect(activeCount(filters)).toBe(9)
  })

  it("ignore les valeurs invalides de l'URL", () => {
    const filters = fromQuery({
      mode: 'sql',
      tags: '1,abc,-2,1,0,4.5,9',
      cree_du: '01/02/2025',
      proprio: 'admin',
      tri: 'password',
      correspondant: ['5', '6'],
    })
    expect(filters.mode).toBe('titre_contenu')
    expect(filters.tags).toEqual([1, 9])
    expect(filters.creeDu).toBe('')
    expect(filters.proprietaire).toBe('tous')
    expect(filters.tri).toBe('created')
    expect(filters.correspondants).toEqual([5])
  })

  it('traduit le texte selon le mode', () => {
    expect(api({ texte: ' edf ' }).title_content).toBe('edf')
    expect(api({ texte: 'edf', mode: 'titre' }).title__icontains).toBe('edf')
    expect(api({ texte: 'edf', mode: 'avance' }).query).toBe('edf')
  })

  it('traduit étiquettes, relations et « Aucun »', () => {
    expect(api({ tags: [1, 2] }).tags__id__all).toBe('1,2')
    expect(api({ tags: [1, 2], tagsMode: 'un' }).tags__id__in).toBe('1,2')
    expect(api({ tags: 'aucun' }).is_tagged).toBe('0')
    expect(api({ correspondants: [5] }).correspondent__id__in).toBe('5')
    expect(api({ types: 'aucun' }).document_type__isnull).toBe('1')
    expect(api({ chemins: [8, 9] }).storage_path__id__in).toBe('8,9')
    expect(api({ champs: [2], champValeur: 'x' })).toMatchObject({ custom_fields__id__all: '2', custom_fields__icontains: 'x' })
  })

  it('traduit dates et propriétaire', () => {
    expect(api({ creeDu: '2025-01-01', ajouteAu: '2025-02-01' })).toMatchObject({
      created__gte: '2025-01-01',
      added__date__lte: '2025-02-01',
    })
    expect(api({ proprietaire: 'moi' }).owner__id).toBe('7')
    expect(api({ proprietaire: 'partages' })).toMatchObject({ owner__id__none: '7', owner__isnull: '0' })
    expect(api({ proprietaire: 'aucun' }).owner__isnull).toBe('1')
    expect(api({ proprietaire: 'moi' }, null).owner__id).toBeUndefined()
  })

  it('tri, pertinence et pagination', () => {
    expect(api({})).toMatchObject({ ordering: '-created', page: '2', page_size: '25' })
    expect(api({ tri: 'title', desc: false }).ordering).toBe('title')
    expect(api({ tri: 'pertinence', mode: 'avance', texte: 'edf' }).ordering).toBeUndefined()
    expect(api({ tri: 'pertinence' }).ordering).toBe('-created')
  })
})
