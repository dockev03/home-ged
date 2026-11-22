import type { HistoryEntry } from '../api/documents'

/** Ligne lisible d'une entrée d'historique : champ, avant → après, ou texte seul. */
export interface HistoryLine {
  label: string
  before?: string
  after?: string
  text?: string
}

export interface HistoryItem {
  id: number
  timestamp: string
  actor: string
  action: string
  lines: HistoryLine[]
}

/** Noms des éléments référencés par identifiant (correspondant, type, chemin, propriétaire). */
export interface HistoryLookup {
  correspondent?: Map<number, { name: string }>
  document_type?: Map<number, { name: string }>
  storage_path?: Map<number, { name: string }>
  owner?: Map<number, { name: string }>
}

const ACTIONS: Record<string, string> = {
  create: 'Création',
  update: 'Modification',
  delete: 'Suppression',
  access: 'Consultation',
}

const FIELDS: Record<string, string> = {
  title: 'Titre',
  content: 'Contenu',
  created: 'Date du document',
  modified: 'Date de modification',
  added: "Date d'ajout",
  correspondent: 'Correspondant',
  document_type: 'Type de document',
  storage_path: 'Chemin de stockage',
  archive_serial_number: "Numéro d'archive",
  owner: 'Propriétaire',
  tags: 'Étiquettes',
  notes: 'Notes',
  custom_fields: 'Champ personnalisé',
  filename: 'Nom du fichier',
  archive_filename: "Nom du fichier d'archive",
  original_filename: 'Nom du fichier original',
  mime_type: 'Type MIME',
  checksum: 'Empreinte',
  archive_checksum: "Empreinte de l'archive",
  page_count: 'Pages',
  version_label: 'Libellé de version',
  deleted_at: 'Mise à la corbeille',
  // Entrées ajoutées par Paperless lui-même (notes, versions).
  'Note Added': 'Note ajoutée (n°)',
  'Note Deleted': 'Note supprimée (n°)',
  'Version Added': 'Nouvelle version (n°)',
  'Version Deleted': 'Version supprimée (n°)',
}

/**
 * Champs techniques que Paperless journalise (relations internes, empreintes, stockage) : sans
 * intérêt pour l'utilisateur, ils noieraient les vraies modifications.
 */
const HIDDEN = new Set([
  'id',
  'share_links',
  'barcodes',
  'versions',
  'workflow_runs',
  'root_document',
  'restored_at',
  'checksum',
  'archive_checksum',
  'storage_type',
  'filename',
  'archive_filename',
  'thumbnail_file',
  'version_index',
  'modified',
])

/** Représentation d'une relation vide par django-auditlog (« documents.Note.None »). */
const RELATION = /^[a-z_]+\.[A-Za-z]+\.None$/

/** Au-delà, la valeur (contenu OCR…) est tronquée. */
const MAX_VALUE = 200

function value(field: string, raw: unknown, lookup: HistoryLookup): string {
  if (raw === null || raw === undefined || raw === 'None' || raw === '') return '(vide)'
  const text = String(raw)
  const names = lookup[field as keyof HistoryLookup]
  if (names && /^\d+$/.test(text)) return names.get(Number(text))?.name ?? `n° ${text}`
  return text.length > MAX_VALUE ? `${text.slice(0, MAX_VALUE)}…` : text
}

function line(field: string, change: unknown, lookup: HistoryLookup): HistoryLine {
  const label = FIELDS[field] ?? field
  if (Array.isArray(change) && change.length === 2) {
    return { label, before: value(field, change[0], lookup), after: value(field, change[1], lookup) }
  }
  if (change && typeof change === 'object') {
    const c = change as { type?: string; operation?: string; objects?: unknown[]; field?: string; value?: unknown }
    if (c.type === 'm2m') {
      const verb = c.operation === 'add' ? 'ajout' : c.operation === 'delete' ? 'retrait' : (c.operation ?? 'modification')
      return { label, text: `${verb} : ${(c.objects ?? []).map(String).join(', ')}` }
    }
    if (c.type === 'custom_field') return { label: c.field ?? label, text: value('', c.value, lookup) }
  }
  return { label, text: value(field, change, lookup) }
}

function isNoise(change: unknown): boolean {
  const values = Array.isArray(change) ? change : [change]
  return values.some((v) => typeof v === 'string' && RELATION.test(v))
}

/** Entrées du journal d'audit, mises en forme en français, la plus récente d'abord. */
export function describeHistory(entries: HistoryEntry[], lookup: HistoryLookup = {}): HistoryItem[] {
  return [...entries]
    .sort((a, b) => Date.parse(b.timestamp) - Date.parse(a.timestamp))
    .map((entry) => ({
      id: entry.id,
      timestamp: entry.timestamp,
      actor: entry.actor?.username ?? 'Système',
      action: ACTIONS[entry.action.toLowerCase()] ?? entry.action,
      lines: Object.entries(entry.changes ?? {})
        .filter(([field, change]) => !HIDDEN.has(field) && !isNoise(change))
        .map(([field, change]) => line(field, change, lookup))
        .filter((l) => !(l.before !== undefined && l.before === l.after)),
    }))
    // Modification dont il ne reste rien après le tri des champs techniques.
    .filter((item) => item.lines.length > 0 || item.action !== 'Modification')
}
