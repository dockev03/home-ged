import { describe, expect, it } from 'vitest'
import { deleteConsequence, kindBySlug, matchingLabel, usesMatchText } from './kinds'

describe('gestion/kinds', () => {
  it('retrouve la liste par son adresse', () => {
    expect(kindBySlug('etiquettes')?.kind).toBe('tags')
    expect(kindBySlug('champs')?.matchable).toBe(false)
    expect(kindBySlug('utilisateurs')).toBeNull()
  })

  it('résume le classement automatique', () => {
    expect(matchingLabel({ id: 1, name: 'EDF', matching_algorithm: 1, match: 'edf électricité' })).toBe('Un des mots : edf électricité')
    expect(matchingLabel({ id: 1, name: 'EDF', matching_algorithm: 6, match: 'ignoré' })).toBe('Automatique')
    expect(matchingLabel({ id: 1, name: 'Champ' })).toBe('')
    expect(usesMatchText(0)).toBe(false)
    expect(usesMatchText(5)).toBe(true)
  })

  it('annonce la conséquence d’une suppression sans toucher aux documents', () => {
    const tags = kindBySlug('etiquettes')!
    const champs = kindBySlug('champs')!
    expect(deleteConsequence(tags, { id: 1, name: 'Banque', document_count: 3 })).toBe(
      'Cet élément sera retiré de 3 documents. Les documents eux-mêmes sont conservés.',
    )
    expect(deleteConsequence(tags, { id: 1, name: 'Banque', document_count: 0 })).toBe('Aucun document n’y est rattaché.')
    expect(deleteConsequence(champs, { id: 1, name: 'Personne', document_count: 1 })).toContain('effacées de 1 document.')
  })
})
