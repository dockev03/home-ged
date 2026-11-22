import type { CustomFieldValue, DocumentDetail, DocumentUpdate } from '../api/documents'

/** État du formulaire « Détails » de la fiche (UI-05). */
export interface DocumentForm {
  title: string
  archive_serial_number: number | null
  created: string
  correspondent: number | null
  document_type: number | null
  storage_path: number | null
  tags: number[]
  custom_fields: CustomFieldValue[]
}

export function toForm(doc: DocumentDetail): DocumentForm {
  return {
    title: doc.title,
    archive_serial_number: doc.archive_serial_number,
    // Paperless renvoie une date (AAAA-MM-JJ) ; on ignore une éventuelle heure.
    created: doc.created.slice(0, 10),
    correspondent: doc.correspondent,
    document_type: doc.document_type,
    storage_path: doc.storage_path,
    tags: [...doc.tags],
    custom_fields: doc.custom_fields.map((f) => ({ field: f.field, value: structuredClone(f.value) })),
  }
}

const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)

/** Champs modifiés uniquement : ce que la fiche envoie à Paperless. */
export function diff(original: DocumentForm, form: DocumentForm): DocumentUpdate {
  const update: DocumentUpdate = {}
  if (form.title.trim() !== original.title) update.title = form.title.trim()
  if (form.archive_serial_number !== original.archive_serial_number)
    update.archive_serial_number = form.archive_serial_number
  if (form.created !== original.created) update.created = form.created
  if (form.correspondent !== original.correspondent) update.correspondent = form.correspondent
  if (form.document_type !== original.document_type) update.document_type = form.document_type
  if (form.storage_path !== original.storage_path) update.storage_path = form.storage_path
  if (!same([...form.tags].sort(), [...original.tags].sort())) update.tags = [...form.tags]
  if (!same(form.custom_fields, original.custom_fields)) update.custom_fields = form.custom_fields
  return update
}

export const isDirty = (original: DocumentForm, form: DocumentForm) => Object.keys(diff(original, form)).length > 0

/** Erreur bloquante avant envoi, ou null. */
export function validate(form: DocumentForm): string | null {
  if (!form.title.trim()) return 'Le titre est obligatoire.'
  if (form.title.trim().length > 128) return 'Le titre ne doit pas dépasser 128 caractères.'
  if (!/^\d{4}-\d{2}-\d{2}$/.test(form.created)) return 'La date du document est obligatoire.'
  return null
}

/** Montant Paperless : « EUR12.50 » (ou nombre seul, ancien format). */
export function parseMonetary(value: unknown, defaultCurrency = 'EUR'): { currency: string; amount: string } {
  const text = value === null || value === undefined ? '' : String(value)
  const match = /^([A-Z]{3})?(-?\d+(?:\.\d+)?)$/.exec(text)
  return match ? { currency: match[1] ?? defaultCurrency, amount: match[2]! } : { currency: defaultCurrency, amount: '' }
}

export function formatMonetary(currency: string, amount: string): string | null {
  const number = Number(amount.replace(',', '.'))
  if (amount.trim() === '' || !Number.isFinite(number)) return null
  return `${currency.toUpperCase()}${number.toFixed(2)}`
}
