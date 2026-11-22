/*
 * « Partager vers Home-GED » (PWA-11), chargé par le service worker généré (workbox.importScripts).
 *
 * Android envoie les fichiers partagés en POST multipart vers /partager (share_target du
 * manifeste). Ce POST ne passe pas par le BFF : le service worker range les fichiers sur
 * l'appareil (IndexedDB, magasin « partages ») puis ouvre l'écran « Documents partagés », qui
 * les envoie à Paperless avec la session de l'utilisateur. Fonctionne aussi hors ligne.
 *
 * Base et magasins : mêmes valeurs que src/offline/db.ts (vérifié par shared.spec.ts).
 */
const PARTAGE_DB_NAME = 'home-ged'
const PARTAGE_DB_VERSION = 2
const PARTAGE_STORES = ['documents', 'fichiers', 'envois', 'session', 'partages']
const PARTAGE_FIELD = 'fichiers'

function partageOpenDb() {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(PARTAGE_DB_NAME, PARTAGE_DB_VERSION)
    req.onupgradeneeded = () => {
      for (const name of PARTAGE_STORES) {
        if (!req.result.objectStoreNames.contains(name)) req.result.createObjectStore(name, { keyPath: 'key' })
      }
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
    req.onblocked = () => reject(new Error('Base bloquée par un autre onglet.'))
  })
}

async function partageSave(files) {
  const db = await partageOpenDb()
  try {
    await new Promise((resolve, reject) => {
      const transaction = db.transaction('partages', 'readwrite')
      const store = transaction.objectStore('partages')
      const now = Date.now()
      files.forEach((file, index) => {
        store.put({
          key: self.crypto.randomUUID(),
          name: file.name || 'document',
          type: file.type,
          lastModified: file.lastModified || now,
          blob: file,
          // Ordre du partage conservé pour des fichiers reçus dans la même milliseconde.
          receivedAt: now + index / 1000,
        })
      })
      transaction.oncomplete = () => resolve()
      transaction.onerror = () => reject(transaction.error)
      transaction.onabort = () => reject(transaction.error || new Error('Transaction annulée.'))
    })
  } finally {
    db.close()
  }
}

function partageRedirect(query) {
  return Response.redirect(new URL(`/partager?${query}`, self.location.origin).href, 303)
}

async function partageReceive(request) {
  try {
    const form = await request.formData()
    const files = form.getAll(PARTAGE_FIELD).filter((value) => value instanceof File)
    if (files.length === 0) return partageRedirect('erreur=aucun-fichier')
    await partageSave(files)
    return partageRedirect(`recus=${files.length}`)
  } catch {
    return partageRedirect('erreur=reception')
  }
}

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'POST') return
  const url = new URL(request.url)
  if (url.origin !== self.location.origin || url.pathname !== '/partager') return
  event.respondWith(partageReceive(request))
})
