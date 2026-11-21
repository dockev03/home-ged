/** Erreur d'appel au BFF, avec le code HTTP. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** GET JSON sur le BFF (même origine : le cookie de session suit automatiquement). */
export async function getJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...init?.headers },
  })
  if (!response.ok) {
    throw new ApiError(response.status, `Erreur ${response.status} sur ${path}`)
  }
  return (await response.json()) as T
}

export interface AppInfo {
  version: string
}

export const getAppInfo = () => getJson<AppInfo>('/api/info')
