import { describe, expect, it } from 'vitest'
import type { CustomField } from '../api/documents'
import { findPersonField, movePage, scanFileName } from './document'

const pages = [{ id: 1 }, { id: 2 }, { id: 3 }]
const ids = (list: { id: number }[]) => list.map((p) => p.id)

describe('movePage', () => {
  it('déplace une page avant ou après, sans modifier la liste d’origine', () => {
    expect(ids(movePage(pages, 2, -1))).toEqual([2, 1, 3])
    expect(ids(movePage(pages, 2, 1))).toEqual([1, 3, 2])
    expect(ids(pages)).toEqual([1, 2, 3])
  })

  it('ne fait rien aux extrémités ou pour une page inconnue', () => {
    expect(ids(movePage(pages, 1, -1))).toEqual([1, 2, 3])
    expect(ids(movePage(pages, 3, 1))).toEqual([1, 2, 3])
    expect(ids(movePage(pages, 9, 1))).toEqual([1, 2, 3])
  })
})

describe('scanFileName', () => {
  const date = new Date(2026, 10, 2, 9, 5)

  it('utilise le titre, débarrassé des caractères interdits', () => {
    expect(scanFileName('  Facture EDF : novembre  ', date)).toBe('Facture EDF - novembre.pdf')
    expect(scanFileName('a/b\\c*?"<>|d\ne', date)).toBe('a-b-c-d-e.pdf')
  })

  it('limite la longueur du nom', () => {
    expect(scanFileName('x'.repeat(300), date)).toBe(`${'x'.repeat(100)}.pdf`)
  })

  it('date le scan quand aucun titre n’est saisi', () => {
    expect(scanFileName('   ', date)).toBe('Scan du 02-11-2026 à 09h05.pdf')
  })
})

describe('findPersonField', () => {
  const field = (name: string, data_type: CustomField['data_type'] = 'select'): CustomField => ({ id: name.length, name, data_type })

  it('trouve la liste « Personne », sans tenir compte de la casse ni des accents', () => {
    expect(findPersonField([field('Montant', 'monetary'), field(' PERSONNE ')])?.name).toBe(' PERSONNE ')
    expect(findPersonField([field('Pérsonne')])?.name).toBe('Pérsonne')
  })

  it('ignore un champ « Personne » qui n’est pas une liste, et renvoie null sinon', () => {
    expect(findPersonField([field('Personne', 'string')])).toBeNull()
    expect(findPersonField([])).toBeNull()
  })
})
