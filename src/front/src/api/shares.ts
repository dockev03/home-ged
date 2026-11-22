import { getJson, sendJson } from './http'

/** Lien de partage protégé d'un document (F-SHARE), vu par son propriétaire. */
export interface ShareLink {
  id: string
  document: number
  /** Titre actuel du document ; null s'il a été supprimé ou n'est plus accessible. */
  titre: string | null
  creeLe: string
  expireLe: string
  /** Nombre maximal de téléchargements ; null sans limite. */
  telechargementsMax: number | null
  telechargements: number
}

export interface ShareSettings {
  /** Durée de validité maximale d'un lien, en jours. */
  joursMax: number
}

export interface NewShare {
  document: number
  motDePasse: string
  jours: number
  telechargementsMax: number | null
}

export const PASSWORD_MIN_LENGTH = 8
export const PASSWORD_MAX_LENGTH = 128
export const MAX_DOWNLOADS_LIMIT = 1000

export const getShareSettings = () => getJson<ShareSettings>('/api/partages/reglages')

export const listShares = () => getJson<{ results: ShareLink[] }>('/api/partages').then((r) => r.results)

export const createShare = (share: NewShare) => sendJson<ShareLink>('POST', '/api/partages', share)

export const revokeShare = (id: string) => sendJson('DELETE', `/api/partages/${encodeURIComponent(id)}`)

/** Adresse publique du lien, à transmettre au destinataire (sans le mot de passe). */
export const shareUrl = (id: string, origin: string = window.location.origin) => `${origin}/s/${id}`

/** Durées proposées à la création, bornées par le maximum configuré (qui est toujours proposé). */
export function durationChoices(maxDays: number): number[] {
  const choices = [1, 7, 14, 30, 90].filter((d) => d < maxDays)
  return [...choices, maxDays]
}

/** Nombre de téléchargements atteint : le lien ne sert plus. */
export const isExhausted = (link: ShareLink) =>
  link.telechargementsMax !== null && link.telechargements >= link.telechargementsMax

/** Sans I, l, O, 0 ni 1 : le mot de passe se dicte et se recopie sans erreur. */
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789'

/** Mot de passe aléatoire (générateur cryptographique du navigateur), sans biais de tirage. */
export function generatePassword(length = 16): string {
  const limit = 256 - (256 % ALPHABET.length)
  let result = ''
  while (result.length < length) {
    for (const byte of crypto.getRandomValues(new Uint8Array(length * 2))) {
      if (byte < limit && result.length < length) result += ALPHABET[byte % ALPHABET.length]
    }
  }
  return result
}
