import { getJson, sendJson } from './http'

/** Document dans la corbeille de Paperless (UI-10). */
export interface TrashedDocument {
  id: number
  title: string
  correspondent: number | null
  document_type: number | null
  tags: number[]
  /** Date du document (AAAA-MM-JJ). */
  created: string
  /** Date de mise à la corbeille (ISO 8601). */
  deleted_at: string
}

export interface TrashPage {
  count: number
  /** Jours de conservation avant effacement automatique ; null si Paperless ne l'indique pas. */
  delai: number | null
  results: TrashedDocument[]
}

export const TRASH_PAGE_SIZE = 25

export const listTrash = (page = 1) => getJson<TrashPage>(`/api/corbeille?page=${page}`)

export const restoreDocuments = (documents: number[]) => sendJson('POST', '/api/corbeille/restauration', { documents })

/** Effacement définitif des documents choisis. */
export const deleteForever = (documents: number[]) => sendJson('POST', '/api/corbeille/suppression', { documents })

/** Effacement définitif de toute la corbeille visible par l'utilisateur. */
export const emptyTrash = () => sendJson('POST', '/api/corbeille/suppression', { tout: true })

/** Date d'effacement automatique, ou null si le délai est inconnu. */
export function purgeDate(deletedAt: string, delay: number | null): Date | null {
  if (delay === null) return null
  const date = new Date(deletedAt)
  if (Number.isNaN(date.getTime())) return null
  date.setDate(date.getDate() + delay)
  return date
}

/** Jours restants avant l'effacement automatique (0 au plus tôt). */
export function daysLeft(deletedAt: string, delay: number | null, now = new Date()): number | null {
  const date = purgeDate(deletedAt, delay)
  if (!date) return null
  return Math.max(0, Math.ceil((date.getTime() - now.getTime()) / 86_400_000))
}
