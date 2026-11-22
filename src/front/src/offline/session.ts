import type { CurrentUser } from '../api/auth'
import { clearStores, offlineDb, STORE_NAMES, type Keyed } from './db'

/**
 * Propriétaire des données hors ligne de l'appareil. Tout ce qui est stocké appartient au dernier
 * utilisateur connecté : un autre compte qui se connecte efface tout avant de commencer.
 */
interface SessionRecord extends Keyed {
  key: 'utilisateur'
  id: number
  username: string
  /** Session expirée ou révoquée : plus de démarrage hors ligne avant une nouvelle connexion. */
  expired?: boolean
}

const KEY = 'utilisateur'

/** Utilisateur connecté (session vérifiée par le BFF) : l'appareil lui appartient désormais. */
export async function rememberUser(user: CurrentUser) {
  try {
    const previous = await offlineDb.get<SessionRecord>('session', KEY)
    if (previous && previous.id !== user.id) await clearStores(STORE_NAMES)
    await offlineDb.put('session', { key: KEY, id: user.id, username: user.username } satisfies SessionRecord)
  } catch {
    // Stockage indisponible : pas de hors-ligne pour cette session.
  }
}

/** Dernier utilisateur connecté sur l'appareil, pour démarrer hors ligne ; null s'il n'y en a pas. */
export async function lastUser(): Promise<CurrentUser | null> {
  try {
    const record = await offlineDb.get<SessionRecord>('session', KEY)
    return record && !record.expired ? { id: record.id, username: record.username } : null
  } catch {
    return null
  }
}

/** Déconnexion : rien ne reste sur l'appareil. */
export async function forgetEverything() {
  await clearStores(STORE_NAMES)
}

/**
 * Session expirée ou révoquée : les documents gardés pour la lecture hors ligne sont effacés et le
 * démarrage hors ligne n'est plus possible. Les envois en attente sont gardés pour partir quand le
 * même utilisateur se reconnectera ; un autre compte les effacera (voir rememberUser).
 */
export async function expireSession() {
  await clearStores(['documents', 'fichiers'])
  try {
    const record = await offlineDb.get<SessionRecord>('session', KEY)
    if (record) await offlineDb.put('session', { ...record, expired: true } satisfies SessionRecord)
  } catch {
    // Stockage indisponible : rien à marquer.
  }
}
