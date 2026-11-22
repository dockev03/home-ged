import { getJson, sendJson } from './http'

/**
 * Page publique d'un lien de partage (SHARE-03), sans session. Après le bon mot de passe, le BFF
 * pose un cookie de ticket limité au chemin du lien : l'aperçu et le téléchargement le joignent
 * d'eux-mêmes, sans que le mot de passe ni un jeton apparaisse dans une URL.
 */
export interface PublicShare {
  titre: string
  expireLe: string
  /** Téléchargements encore possibles ; null sans limite. */
  telechargementsRestants: number | null
}

/** Codes d'erreur du BFF propres à la page publique. */
export const UNAVAILABLE = 'lien_indisponible'
export const PASSWORD_REQUIRED = 'mot_de_passe_requis'

const base = (id: string) => `/api/public/partages/${encodeURIComponent(id)}`

/** Informations du lien si le ticket est encore valable (rechargement de la page) ; sinon 401. */
export const getPublicShare = (id: string) => getJson<PublicShare>(base(id), { ignoreUnauthorized: true })

export const openPublicShare = (id: string, motDePasse: string) =>
  sendJson<PublicShare>('POST', `${base(id)}/acces`, { motDePasse }, { ignoreUnauthorized: true })

export const publicPreviewUrl = (id: string) => `${base(id)}/apercu`

export const publicDownloadUrl = (id: string) => `${base(id)}/telechargement`
