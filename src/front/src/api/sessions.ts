import { getJson, sendJson } from './http'

/** Session active de l'utilisateur, telle que renvoyée par le BFF (dates ISO 8601). */
export interface SessionInfo {
  id: string
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  ip: string | null
  userAgent: string | null
  /** Session de cet appareil. */
  current: boolean
}

export const listSessions = () => getJson<SessionInfo[]>('/api/auth/sessions')

/** Révoque une session ; ApiError.code session_introuvable si elle n'existe plus. */
export const revokeSession = (id: string) => sendJson('DELETE', `/api/auth/sessions/${encodeURIComponent(id)}`)
