import { describe, expect, it } from 'vitest'
import type { DocumentDetail } from '../api/documents'
import { diff, formatMonetary, isDirty, parseMonetary, toForm, validate } from './form'

const doc = {
  id: 5,
  title: 'Facture',
  content: '',
  tags: [2, 1],
  correspondent: 3,
  document_type: null,
  storage_path: null,
  created: '2026-11-02',
  added: '2026-11-03T10:00:00Z',
  modified: '2026-11-03T10:00:00Z',
  archive_serial_number: null,
  custom_fields: [{ field: 1, value: 'EUR12.50' }],
  notes: [],
  owner: 1,
  original_file_name: 'f.pdf',
  archived_file_name: 'f.pdf',
  mime_type: 'application/pdf',
} satisfies DocumentDetail

describe('formulaire de la fiche', () => {
  it("n'envoie que les champs modifiés", () => {
    const original = toForm(doc)
    const form = toForm(doc)
    expect(isDirty(original, form)).toBe(false)

    form.title = ' Facture EDF '
    form.tags = [1, 2] // même ensemble, autre ordre : inchangé
    form.correspondent = null
    form.custom_fields[0]!.value = 'EUR13.00'

    expect(diff(original, form)).toEqual({
      title: 'Facture EDF',
      correspondent: null,
      custom_fields: [{ field: 1, value: 'EUR13.00' }],
    })
  })

  it('le formulaire est une copie : modifier ne touche pas le document', () => {
    const form = toForm(doc)
    form.tags.push(9)
    form.custom_fields[0]!.value = 'x'
    expect(doc.tags).toEqual([2, 1])
    expect(doc.custom_fields[0]!.value).toBe('EUR12.50')
  })

  it('valide titre et date', () => {
    expect(validate({ ...toForm(doc), title: '  ' })).toBe('Le titre est obligatoire.')
    expect(validate({ ...toForm(doc), created: '' })).toBe('La date du document est obligatoire.')
    expect(validate(toForm(doc))).toBeNull()
  })

  it('lit et écrit les montants', () => {
    expect(parseMonetary('EUR12.50')).toEqual({ currency: 'EUR', amount: '12.50' })
    expect(parseMonetary('12.5', 'USD')).toEqual({ currency: 'USD', amount: '12.5' })
    expect(parseMonetary(null)).toEqual({ currency: 'EUR', amount: '' })
    expect(formatMonetary('eur', '12,5')).toBe('EUR12.50')
    expect(formatMonetary('EUR', '')).toBeNull()
    expect(formatMonetary('EUR', 'abc')).toBeNull()
  })
})
