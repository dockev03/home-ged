import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import serviceWorkerSource from '../../public/partage-sw.js?raw'
import { DB_NAME, DB_VERSION, memoryBackend, offlineDb, setOfflineBackend, STORE_NAMES } from './db'
import { listSharedFiles, removeSharedFiles, type SharedFileRecord } from './shared'

const record = (key: string, receivedAt: number, name = `${key}.pdf`): SharedFileRecord => ({
  key,
  name,
  type: 'application/pdf',
  lastModified: 1000,
  blob: new Blob([`contenu ${key}`]),
  receivedAt,
})

describe('fichiers partagés (stockage)', () => {
  beforeEach(() => setOfflineBackend(memoryBackend()))
  afterEach(() => setOfflineBackend(null))

  it('rend les fichiers reçus dans l’ordre de réception', async () => {
    await offlineDb.put('partages', record('b', 2))
    await offlineDb.put('partages', record('a', 1, 'facture.pdf'))

    const files = await listSharedFiles()

    expect(files.map((f) => f.key)).toEqual(['a', 'b'])
    expect(files[0]!.file.name).toBe('facture.pdf')
    expect(files[0]!.file.type).toBe('application/pdf')
    expect(await files[0]!.file.text()).toBe('contenu a')
  })

  it('retire les fichiers envoyés ou abandonnés', async () => {
    await offlineDb.put('partages', record('a', 1))
    await offlineDb.put('partages', record('b', 2))

    await removeSharedFiles(['a'])

    expect((await listSharedFiles()).map((f) => f.key)).toEqual(['b'])
  })

  it('rend une liste vide sans stockage', async () => {
    setOfflineBackend(null)
    expect(await listSharedFiles()).toEqual([])
  })
})

/** IndexedDB réduite à ce qu'utilise le script du service worker. */
function fakeIndexedDb(existing: string[]) {
  const stores = new Map<string, Map<string, Record<string, unknown>>>(existing.map((name) => [name, new Map()]))
  const opened: { name: string; version: number }[] = []
  const indexedDB = {
    open(name: string, version: number) {
      opened.push({ name, version })
      const req: Record<string, unknown> & { onupgradeneeded?: () => void; onsuccess?: () => void } = {}
      setTimeout(() => {
        req.result = {
          objectStoreNames: { contains: (store: string) => stores.has(store) },
          createObjectStore: (store: string) => stores.set(store, new Map()),
          transaction(store: string) {
            const tx: { oncomplete?: () => void; objectStore: () => unknown } = {
              objectStore: () => ({ put: (value: { key: string }) => stores.get(store)!.set(value.key, value) }),
            }
            setTimeout(() => tx.oncomplete?.())
            return tx
          },
          close() {},
        }
        req.onupgradeneeded?.()
        req.onsuccess?.()
      })
      return req
    },
  }
  return { indexedDB, stores, opened }
}

type FetchListener = (event: { request: unknown; respondWith: (r: Promise<Response>) => void }) => void

/** Exécute le script du service worker dans un contexte simulé ; rend son écouteur fetch. */
function loadServiceWorker(indexedDB: unknown) {
  let listener: FetchListener | null = null
  let uuid = 0
  const self = {
    location: { origin: 'https://ged.test' },
    crypto: { randomUUID: () => `uuid-${++uuid}` },
    addEventListener: (type: string, fn: FetchListener) => {
      if (type === 'fetch') listener = fn
    },
  }
  new Function('self', 'indexedDB', serviceWorkerSource)(self, indexedDB)
  return listener as unknown as FetchListener
}

/** Envoie une requête à l'écouteur ; rend la réponse, ou null s'il la laisse passer. */
async function dispatch(listener: FetchListener, request: { method: string; url: string; formData?: () => Promise<unknown> }) {
  const responses: Promise<Response>[] = []
  listener({ request, respondWith: (r) => responses.push(r) })
  return responses.length ? await responses[0]! : null
}

const post = (fields: Record<string, unknown[]>, url = 'https://ged.test/partager') => ({
  method: 'POST',
  url,
  formData: async () => ({ getAll: (name: string) => fields[name] ?? [] }),
})

describe('réception par le service worker (public/partage-sw.js)', () => {
  it('utilise la même base et les mêmes magasins que l’application', () => {
    expect(serviceWorkerSource).toContain(`const PARTAGE_DB_NAME = '${DB_NAME}'`)
    expect(serviceWorkerSource).toContain(`const PARTAGE_DB_VERSION = ${DB_VERSION}`)
    expect(serviceWorkerSource).toContain(`const PARTAGE_STORES = [${STORE_NAMES.map((s) => `'${s}'`).join(', ')}]`)
  })

  it('range les fichiers partagés puis ouvre l’écran « Documents partagés »', async () => {
    const db = fakeIndexedDb(['documents', 'fichiers', 'envois', 'session'])
    const listener = loadServiceWorker(db.indexedDB)
    const files = [new File(['a'], 'facture.pdf', { type: 'application/pdf' }), new File(['b'], 'photo.jpg', { type: 'image/jpeg' })]

    const response = await dispatch(listener, post({ fichiers: [...files, 'texte ignoré'] }))

    expect(response!.status).toBe(303)
    expect(response!.headers.get('location')).toBe('https://ged.test/partager?recus=2')
    expect(db.opened).toEqual([{ name: DB_NAME, version: DB_VERSION }])
    const saved = [...db.stores.get('partages')!.values()]
    expect(saved.map((r) => [r.key, r.name, r.type])).toEqual([
      ['uuid-1', 'facture.pdf', 'application/pdf'],
      ['uuid-2', 'photo.jpg', 'image/jpeg'],
    ])
    expect(saved[0]!.blob).toBe(files[0])
    expect(saved[0]!.receivedAt as number).toBeLessThan(saved[1]!.receivedAt as number)
  })

  it('signale un partage sans fichier', async () => {
    const listener = loadServiceWorker(fakeIndexedDb([]).indexedDB)

    const response = await dispatch(listener, post({ fichiers: [] }))

    expect(response!.headers.get('location')).toBe('https://ged.test/partager?erreur=aucun-fichier')
  })

  it('signale un échec de lecture ou de stockage', async () => {
    const listener = loadServiceWorker(fakeIndexedDb([]).indexedDB)
    const request = { method: 'POST', url: 'https://ged.test/partager', formData: () => Promise.reject(new TypeError()) }

    const response = await dispatch(listener, request)

    expect(response!.headers.get('location')).toBe('https://ged.test/partager?erreur=reception')
  })

  it.each([
    { method: 'GET', url: 'https://ged.test/partager' },
    { method: 'POST', url: 'https://ged.test/api/documents/upload' },
    { method: 'POST', url: 'https://autre.test/partager' },
  ])('laisse passer $method $url', async (request) => {
    const listener = loadServiceWorker(fakeIndexedDb([]).indexedDB)
    expect(await dispatch(listener, request)).toBeNull()
  })
})
