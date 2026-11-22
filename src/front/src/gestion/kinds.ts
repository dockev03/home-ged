import type { CustomFieldType } from '../api/documents'
import type { ManagedItem, ManagedKind } from '../api/gestion'

/** Écran « Gérer » d'une liste (UI-07) : adresse, libellés et lien vers les documents filtrés. */
export interface KindConfig {
  /** Segment d'adresse : /gerer/etiquettes… */
  slug: string
  kind: ManagedKind
  title: string
  /** « l'étiquette », « le correspondant »… */
  the: string
  /** « Nouvelle étiquette », « Nouveau correspondant »… */
  create: string
  /** Paramètre de la liste des documents filtrée sur cet élément. */
  documentsParam: string
  /** Classement automatique (correspondance) et propriétaire : tout sauf les champs personnalisés. */
  matchable: boolean
}

export const KINDS: readonly KindConfig[] = [
  { slug: 'etiquettes', kind: 'tags', title: 'Étiquettes', the: 'l’étiquette', create: 'Nouvelle étiquette', documentsParam: 'tags', matchable: true },
  { slug: 'correspondants', kind: 'correspondants', title: 'Correspondants', the: 'le correspondant', create: 'Nouveau correspondant', documentsParam: 'correspondant', matchable: true },
  { slug: 'types', kind: 'types', title: 'Types de documents', the: 'le type de documents', create: 'Nouveau type de documents', documentsParam: 'type', matchable: true },
  { slug: 'chemins', kind: 'chemins', title: 'Chemins de stockage', the: 'le chemin de stockage', create: 'Nouveau chemin de stockage', documentsParam: 'chemin', matchable: true },
  { slug: 'champs', kind: 'champs', title: 'Champs personnalisés', the: 'le champ personnalisé', create: 'Nouveau champ personnalisé', documentsParam: 'champs', matchable: false },
]

export const KIND_SLUGS = KINDS.map((k) => k.slug)

export const kindBySlug = (slug: unknown) => KINDS.find((k) => k.slug === slug) ?? null

/** Algorithmes de correspondance de Paperless (valeur 0 à 6). */
export const MATCHING_ALGORITHMS = [
  { value: 6, label: 'Automatique', help: 'Paperless apprend à partir des documents déjà classés.' },
  { value: 1, label: 'Un des mots', help: 'Le document contient au moins un des mots.' },
  { value: 2, label: 'Tous les mots', help: 'Le document contient tous les mots, dans n’importe quel ordre.' },
  { value: 3, label: 'Correspondance exacte', help: 'Le document contient cette suite de caractères.' },
  { value: 4, label: 'Expression régulière', help: 'Le document correspond à cette expression régulière.' },
  { value: 5, label: 'Mot approximatif', help: 'Le document contient un mot proche de celui-ci.' },
  { value: 0, label: 'Aucune', help: 'Jamais attribué automatiquement.' },
] as const

/** Algorithmes qui utilisent le texte de correspondance. */
export const usesMatchText = (algorithm: number | undefined) => algorithm !== undefined && algorithm >= 1 && algorithm <= 5

export const matchingLabel = (item: ManagedItem) => {
  const algorithm = MATCHING_ALGORITHMS.find((a) => a.value === item.matching_algorithm)
  if (!algorithm) return ''
  return usesMatchText(item.matching_algorithm) && item.match ? `${algorithm.label} : ${item.match}` : algorithm.label
}

export const DATA_TYPES: readonly { value: CustomFieldType; label: string }[] = [
  { value: 'string', label: 'Texte' },
  { value: 'longtext', label: 'Texte long' },
  { value: 'url', label: 'Adresse web' },
  { value: 'date', label: 'Date' },
  { value: 'boolean', label: 'Oui / non' },
  { value: 'integer', label: 'Nombre entier' },
  { value: 'float', label: 'Nombre décimal' },
  { value: 'monetary', label: 'Montant' },
  { value: 'select', label: 'Liste de choix' },
  { value: 'documentlink', label: 'Lien vers des documents' },
]

export const dataTypeLabel = (type: CustomFieldType | undefined) => DATA_TYPES.find((t) => t.value === type)?.label ?? ''

const documents = (count: number) => (count === 1 ? '1 document' : `${count} documents`)

/** Conséquence d'une suppression, affichée avant confirmation : aucun document n'est supprimé. */
export function deleteConsequence(config: KindConfig, item: ManagedItem): string {
  const count = item.document_count ?? 0
  if (config.kind === 'champs') {
    return count > 0
      ? `Ses valeurs seront effacées de ${documents(count)}. Les documents eux-mêmes sont conservés.`
      : 'Aucun document n’utilise ce champ.'
  }
  return count > 0
    ? `Cet élément sera retiré de ${documents(count)}. Les documents eux-mêmes sont conservés.`
    : 'Aucun document n’y est rattaché.'
}
