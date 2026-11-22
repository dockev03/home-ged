import type { CustomField } from '../api/documents'
import type { Quad } from './geometry'
import type { ScanFilter } from './protocol'

/** Page d'un document scanné (SCAN-04) : de quoi la recadrer à nouveau, et son rendu JPEG. */
export interface ScanPage {
  id: number
  /** Photo d'origine, gardée dans le navigateur pour un nouveau recadrage. */
  photo: Blob
  detected: Quad | null
  corners: Quad
  filter: ScanFilter
  /** Page redressée et filtrée (JPEG), assemblée dans le PDF. */
  jpeg: Blob
  /** URL `blob:` de `jpeg` pour la vignette, à libérer avec URL.revokeObjectURL. */
  url: string
}

/** Déplace la page d'un cran (−1 : avant, +1 : après) ; inchangé aux extrémités. */
export function movePage<T extends { id: number }>(pages: readonly T[], id: number, delta: -1 | 1): T[] {
  const from = pages.findIndex((page) => page.id === id)
  const to = from + delta
  if (from < 0 || to < 0 || to >= pages.length) return [...pages]
  const result = [...pages]
  ;[result[from], result[to]] = [result[to]!, result[from]!]
  return result
}

const pad = (value: number) => String(value).padStart(2, '0')

/**
 * Nom du PDF envoyé : le titre saisi, sinon « Scan du JJ-MM-AAAA à HHhMM ». Paperless en tire le
 * titre du document quand aucun n'est saisi. Caractères interdits des noms de fichiers remplacés.
 */
export function scanFileName(title: string, date: Date): string {
  const cleaned = title
    .replace(/[\\/:*?"<>|\p{Cc}]+/gu, '-')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 100)
  const base =
    cleaned ||
    `Scan du ${pad(date.getDate())}-${pad(date.getMonth() + 1)}-${date.getFullYear()} à ${pad(date.getHours())}h${pad(date.getMinutes())}`
  return `${base}.pdf`
}

const normalize = (text: string) =>
  text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .trim()
    .toLowerCase()

/** Champ personnalisé « Personne » (B11) : liste de sélection créée à la configuration de Paperless. */
export function findPersonField(fields: readonly CustomField[]): CustomField | null {
  return fields.find((field) => field.data_type === 'select' && normalize(field.name) === 'personne') ?? null
}
