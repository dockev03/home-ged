/**
 * Stockage hors ligne de l'appareil (PWA-09, PWA-10) : petite couche au-dessus d'IndexedDB, sans
 * dépendance. Chaque enregistrement porte sa clé dans le champ `key`.
 *
 * - `documents` : fiches consultées récemment (lecture hors ligne) ;
 * - `fichiers` : aperçu et vignette de ces fiches ;
 * - `envois` : fichiers en attente d'envoi à Paperless ;
 * - `session` : dernier utilisateur connecté sur l'appareil ;
 * - `partages` : fichiers reçus d'une autre application (PWA-11), écrits par le service worker
 *   (`public/partage-sw.js`, qui reprend le nom, la version et la liste des magasins ci-dessous).
 *
 * Sans IndexedDB (navigation privée restreinte, tests), toutes les opérations sont sans effet :
 * l'application fonctionne comme avant, sans hors-ligne.
 */
export type StoreName = 'documents' | 'fichiers' | 'envois' | 'session' | 'partages'

export const STORE_NAMES: readonly StoreName[] = ['documents', 'fichiers', 'envois', 'session', 'partages']

export interface Keyed {
  key: string
}

export interface OfflineBackend {
  get<T extends Keyed>(store: StoreName, key: string): Promise<T | undefined>
  getAll<T extends Keyed>(store: StoreName): Promise<T[]>
  put<T extends Keyed>(store: StoreName, value: T): Promise<void>
  delete(store: StoreName, key: string): Promise<void>
  clear(store: StoreName): Promise<void>
}

export const DB_NAME = 'home-ged'
/** 2 : magasin « partages » (PWA-11). */
export const DB_VERSION = 2

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

/** Backend IndexedDB : une base « home-ged », ouverte à la première utilisation. */
export function indexedDbBackend(factory: IDBFactory = indexedDB): OfflineBackend {
  let opening: Promise<IDBDatabase> | null = null

  function open(): Promise<IDBDatabase> {
    opening ??= new Promise<IDBDatabase>((resolve, reject) => {
      const req = factory.open(DB_NAME, DB_VERSION)
      req.onupgradeneeded = () => {
        for (const name of STORE_NAMES) {
          if (!req.result.objectStoreNames.contains(name)) req.result.createObjectStore(name, { keyPath: 'key' })
        }
      }
      req.onsuccess = () => {
        const db = req.result
        // Nouvelle version ouverte dans un autre onglet : on libère la base, réouverte au besoin.
        db.onversionchange = () => {
          db.close()
          opening = null
        }
        resolve(db)
      }
      req.onerror = () => reject(req.error)
      req.onblocked = () => reject(new Error('Base hors ligne bloquée par un autre onglet.'))
    }).catch((error: unknown) => {
      opening = null
      throw error
    })
    return opening
  }

  async function run<T>(store: StoreName, mode: IDBTransactionMode, action: (s: IDBObjectStore) => IDBRequest<T>) {
    const db = await open()
    const transaction = db.transaction(store, mode)
    const result = request(action(transaction.objectStore(store)))
    await new Promise<void>((resolve, reject) => {
      transaction.oncomplete = () => resolve()
      transaction.onerror = () => reject(transaction.error)
      transaction.onabort = () => reject(transaction.error ?? new Error('Transaction annulée.'))
    })
    return result
  }

  return {
    get: <T extends Keyed>(store: StoreName, key: string) =>
      run(store, 'readonly', (s) => s.get(key) as IDBRequest<T | undefined>),
    getAll: <T extends Keyed>(store: StoreName) => run(store, 'readonly', (s) => s.getAll() as IDBRequest<T[]>),
    put: async (store, value) => {
      await run(store, 'readwrite', (s) => s.put(value))
    },
    delete: async (store, key) => {
      await run(store, 'readwrite', (s) => s.delete(key))
    },
    clear: async (store) => {
      await run(store, 'readwrite', (s) => s.clear())
    },
  }
}

/** Backend en mémoire : pour les tests, ou à défaut d'IndexedDB. */
export function memoryBackend(): OfflineBackend {
  const stores = new Map<StoreName, Map<string, Keyed>>(STORE_NAMES.map((name) => [name, new Map()]))
  const store = (name: StoreName) => stores.get(name)!
  return {
    get: async <T extends Keyed>(name: StoreName, key: string) => store(name).get(key) as T | undefined,
    getAll: async <T extends Keyed>(name: StoreName) => [...store(name).values()] as T[],
    put: async (name, value) => {
      store(name).set(value.key, value)
    },
    delete: async (name, key) => {
      store(name).delete(key)
    },
    clear: async (name) => {
      store(name).clear()
    },
  }
}

let backend: OfflineBackend | null = typeof indexedDB === 'undefined' ? null : indexedDbBackend()

/** Vrai si l'appareil offre un stockage hors ligne. */
export const hasOfflineStorage = () => backend !== null

/** Remplace le stockage (tests) ; null désactive le hors-ligne. */
export function setOfflineBackend(value: OfflineBackend | null) {
  backend = value
}

export const offlineDb: OfflineBackend = {
  get: async (store, key) => backend?.get(store, key),
  getAll: async <T extends Keyed>(store: StoreName) => (backend ? backend.getAll<T>(store) : []),
  put: async (store, value) => backend?.put(store, value),
  delete: async (store, key) => backend?.delete(store, key),
  clear: async (store) => backend?.clear(store),
}

/** Vide les magasins donnés ; une erreur de stockage n'empêche pas de vider les suivants. */
export async function clearStores(stores: readonly StoreName[]) {
  await Promise.allSettled(stores.map((store) => offlineDb.clear(store)))
}

/** Erreur réseau (appareil hors ligne, serveur injoignable) : fetch et XMLHttpRequest rejettent un TypeError. */
export const isNetworkError = (error: unknown) => error instanceof TypeError
