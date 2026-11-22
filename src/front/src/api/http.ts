/** Erreur d'appel au BFF : code HTTP, message affichable et code d'erreur métier éventuel. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(status: number, message: string, code: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

/** En-tête exigé par le BFF sur toute requête qui modifie quelque chose (protection CSRF). */
export const ANTI_FORGERY_HEADER = { 'X-Requested-With': 'HomeGed' } as const

type UnauthorizedHandler = () => void
let unauthorizedHandler: UnauthorizedHandler | null = null

/** Appelé quand le BFF répond 401 : session absente, expirée ou révoquée. */
export function onUnauthorized(handler: UnauthorizedHandler | null) {
  unauthorizedHandler = handler
}

export interface RequestOptions {
  /** Ne pas déclencher le gestionnaire 401 (écran de connexion, vérification de session). */
  ignoreUnauthorized?: boolean
}

async function toApiError(response: Response, path: string): Promise<ApiError> {
  let message = `Erreur ${response.status} sur ${path}`
  let code: string | null = null
  try {
    const problem = (await response.json()) as { title?: unknown; code?: unknown }
    if (typeof problem.title === 'string') message = problem.title
    if (typeof problem.code === 'string') code = problem.code
  } catch {
    // Corps absent ou non JSON : message générique.
  }
  return new ApiError(response.status, message, code)
}

async function send(path: string, init: RequestInit, options: RequestOptions): Promise<Response> {
  const response = await fetch(path, { ...init, credentials: 'same-origin' })
  if (!response.ok) {
    if (response.status === 401 && !options.ignoreUnauthorized) unauthorizedHandler?.()
    throw await toApiError(response, path)
  }
  return response
}

/** GET JSON sur le BFF (même origine : le cookie de session suit automatiquement). */
export async function getJson<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(path, { headers: { Accept: 'application/json' } }, options)
  return (await response.json()) as T
}

/** POST, PUT, DELETE… avec corps JSON facultatif ; renvoie le JSON, ou null si la réponse est vide. */
export async function sendJson<T = null>(
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE',
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...ANTI_FORGERY_HEADER }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const response = await send(
    path,
    { method, headers, body: body === undefined ? undefined : JSON.stringify(body) },
    options,
  )
  if (response.status === 204) return null as T
  const text = await response.text()
  return (text ? JSON.parse(text) : null) as T
}

/** POST JSON dont la réponse est un fichier (archive zip) : renvoie son contenu et son nom. */
export async function postForFile(path: string, body: unknown): Promise<{ blob: Blob; fileName: string | null }> {
  const response = await send(
    path,
    {
      method: 'POST',
      headers: { Accept: '*/*', 'Content-Type': 'application/json', ...ANTI_FORGERY_HEADER },
      body: JSON.stringify(body),
    },
    {},
  )
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const fileName = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? null
  return { blob: await response.blob(), fileName: fileName ? decodeURIComponent(fileName) : null }
}

export interface AppInfo {
  version: string
}

export const getAppInfo = () => getJson<AppInfo>('/api/info')
