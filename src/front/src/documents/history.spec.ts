import { describe, expect, it } from 'vitest'
import { describeHistory } from './history'

describe("mise en forme de l'historique", () => {
  it('traduit actions, champs et identifiants, la plus récente d’abord', () => {
    const items = describeHistory(
      [
        { id: 1, timestamp: '2026-11-01T10:00:00Z', action: 'create', changes: { title: ['None', 'Facture'] }, actor: null },
        {
          id: 2,
          timestamp: '2026-11-02T10:00:00Z',
          action: 'update',
          changes: {
            correspondent: ['None', '4'],
            document_type: ['2', '99'],
            tags: { type: 'm2m', operation: 'add', objects: ['Banque', 'Impôts'] },
            content: ['', 'x'.repeat(300)],
            inconnu: 'valeur',
          },
          actor: { id: 1, username: 'alice' },
        },
        {
          id: 3,
          timestamp: '2026-10-31T10:00:00Z',
          action: 'update',
          changes: { custom_fields: { type: 'custom_field', field: 'Montant', value: 'EUR12.50' } },
          actor: { id: 1, username: 'alice' },
        },
      ],
      { correspondent: new Map([[4, { name: 'EDF' }]]), document_type: new Map() },
    )

    expect(items.map((i) => i.id)).toEqual([2, 1, 3])
    expect(items[1]).toMatchObject({ action: 'Création', actor: 'Système', lines: [{ label: 'Titre', before: '(vide)', after: 'Facture' }] })
    const update = items[0]!
    expect(update.action).toBe('Modification')
    expect(update.lines).toEqual([
      { label: 'Correspondant', before: '(vide)', after: 'EDF' },
      { label: 'Type de document', before: 'n° 2', after: 'n° 99' },
      { label: 'Étiquettes', text: 'ajout : Banque, Impôts' },
      { label: 'Contenu', before: '(vide)', after: `${'x'.repeat(200)}…` },
      { label: 'inconnu', text: 'valeur' },
    ])
    expect(items[2]!.lines).toEqual([{ label: 'Montant', text: 'EUR12.50' }])
  })

  it('masque les champs techniques et les relations vides de la création', () => {
    const [item] = describeHistory(
      [
        {
          id: 9,
          timestamp: '2026-11-22T11:08:31Z',
          action: 'create',
          changes: {
            title: ['None', 'Facture'],
            id: ['None', '6'],
            checksum: ['None', '927cfe'],
            share_links: ['None', 'documents.ShareLink.None'],
            notes: ['None', 'documents.Note.None'],
            document_type: ['None', 'None'],
            owner: ['None', '2'],
          },
          actor: null,
        },
      ],
      { owner: new Map([[2, { name: 'ci' }]]) },
    )
    expect(item!.lines).toEqual([
      { label: 'Titre', before: '(vide)', after: 'Facture' },
      { label: 'Propriétaire', before: '(vide)', after: 'ci' },
    ])
  })

  it("traduit les entrées propres à Paperless et retire les modifications vides", () => {
    const items = describeHistory([
      { id: 1, timestamp: '2026-11-22T11:13:00Z', action: 'update', changes: { 'Version Added': ['None', 7] }, actor: null },
      { id: 2, timestamp: '2026-11-22T11:12:00Z', action: 'update', changes: { checksum: ['a', 'b'] }, actor: null },
    ])
    expect(items).toHaveLength(1)
    expect(items[0]!.lines).toEqual([{ label: 'Nouvelle version (n°)', before: '(vide)', after: '7' }])
  })
})
