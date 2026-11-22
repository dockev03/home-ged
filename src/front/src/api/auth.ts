import { ApiError, getJson, sendJson } from './http'

export interface CurrentUser {
  id: number
  username: string
}

export interface Credentials {
  username: string
  password: string
  /** Code TOTP, seulement si Paperless l'a demandé. */
  code?: string
}

/** Connexion avec les identifiants Paperless ; ApiError.code : mfa_requis, code_invalide… */
export const login = (credentials: Credentials) =>
  sendJson<CurrentUser>('POST', '/api/auth/login', credentials, { ignoreUnauthorized: true })

export const logout = () => sendJson('POST', '/api/auth/logout', undefined, { ignoreUnauthorized: true })

/** Utilisateur connecté, ou null sans session valide. */
export async function getCurrentUser(): Promise<CurrentUser | null> {
  try {
    return await getJson<CurrentUser>('/api/auth/me', { ignoreUnauthorized: true })
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}
