import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DocumentDetail } from '../api/documents'
import { memoryBackend, offlineDb, setOfflineBackend } from './db'
import {
  forgetDocument,
  getOfflineDocument,
  getOfflineFile,
  listOfflineDocuments,
  MAX_OFFLINE_FILE_BYTES,
  rememberDocument,
  toEvict,
} from './documents'

const ALICE = 1
const BOB = 2

function fakeDoc(id: number, extra: Partial<DocumentDetail> = {}): DocumentDetail {
  return {
    id,
    title: `Facture ${id}`,
    content: `Texte ${id}`,
    tags: [],
    correspondent: null,
    document_type: null,
    storage_path: null,
    created: '2026-03-15',
    added: '2026-03-16T10:00:00Z',
    modified: '2026-03-16T10:00:00Z',
    archive_serial_number: null,
    custom_fields: [],
    notes: [],
    owner: ALICE,
    original_file_name: 'facture.pdf',
    archived_file_name: 'facture.pdf',
    mime_type: 'application/pdf',
    versions: [{ id: 10, added: '2026-03-16T10:00:00Z', version_label: null, checksum: 'abc', is_root: true }],
    ...extra,
  }
}

/** Faux BFF : vignette et aperçu de chaque document ; réponses particulières par chemin. */
function stubFiles(responses: Record<string, () => Response> = {}) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input)
    const special = responses[path]
    if (special) return special()
    return new Response(`contenu de ${path}`, { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('documents hors ligne (PWA-09)', () => {
  beforeEach(() => setOfflineBackend(memoryBackend()))
  afterEach(() => {
    setOfflineBackend(null)
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('garde la fiche, la vignette et l’aperçu d’un document consulté', async () => {
    const fetchMock = stubFiles()

    await rememberDocument(ALICE, fakeDoc(4))

    expect(fetchMock.mock.calls.map((c) => String(c[0]))).toEqual(['/api/documents/4/thumb', '/api/documents/4/preview'])
    const copy = await getOfflineDocument(ALICE, 4)
    expect(copy?.document.title).toBe('Facture 4')
    expect(copy?.files).toEqual(['vignette', 'apercu'])
    expect(await (await getOfflineFile(4, 'apercu'))!.text()).toBe('contenu de /api/documents/4/preview')
  })

  it('ne retélécharge les fichiers que si le fichier du document a changé', async () => {
    const fetchMock = stubFiles()
    await rememberDocument(ALICE, fakeDoc(4))
    fetchMock.mockClear()

    await rememberDocument(ALICE, fakeDoc(4, { title: 'Renommé', modified: '2026-03-17T10:00:00Z' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect((await getOfflineDocument(ALICE, 4))?.document.title).toBe('Renommé')
    expect((await getOfflineDocument(ALICE, 4))?.files).toEqual(['vignette', 'apercu'])

    await rememberDocument(ALICE, fakeDoc(4, { versions: [{ id: 11, added: '', version_label: null, checksum: 'def', is_root: false }] }))
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('écarte un aperçu trop gros, et réessaie un téléchargement échoué à la consultation suivante', async () => {
    const big = () => new Response('x', { headers: { 'Content-Length': String(MAX_OFFLINE_FILE_BYTES + 1) } })
    const fetchMock = stubFiles({ '/api/documents/4/preview': big, '/api/documents/5/preview': () => Response.error() })

    await rememberDocument(ALICE, fakeDoc(4))
    await rememberDocument(ALICE, fakeDoc(5))
    expect((await getOfflineDocument(ALICE, 4))?.files).toEqual(['vignette'])
    expect((await getOfflineDocument(ALICE, 5))?.files).toEqual(['vignette'])

    fetchMock.mockClear()
    await rememberDocument(ALICE, fakeDoc(4))
    await rememberDocument(ALICE, fakeDoc(5))
    // Trop gros : inutile de réessayer. Réseau manquant : nouvel essai.
    expect(fetchMock.mock.calls.map((c) => String(c[0]))).toEqual(['/api/documents/5/thumb', '/api/documents/5/preview'])
  })

  it('liste les documents de l’utilisateur, le plus récemment consulté d’abord', async () => {
    stubFiles()
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-22T10:00:00Z'))
    await rememberDocument(ALICE, fakeDoc(4))
    vi.setSystemTime(new Date('2026-11-22T10:01:00Z'))
    await rememberDocument(ALICE, fakeDoc(5))
    vi.setSystemTime(new Date('2026-11-22T10:02:00Z'))
    await rememberDocument(ALICE, fakeDoc(4))

    expect((await listOfflineDocuments(ALICE)).map((d) => d.document.id)).toEqual([4, 5])
    expect(await listOfflineDocuments(BOB)).toEqual([])
    expect(await getOfflineDocument(BOB, 4)).toBeNull()
  })

  it('oublie un document supprimé, avec ses fichiers', async () => {
    stubFiles()
    await rememberDocument(ALICE, fakeDoc(4))

    await forgetDocument(4)

    expect(await getOfflineDocument(ALICE, 4)).toBeNull()
    expect(await offlineDb.getAll('fichiers')).toEqual([])
  })

  it('sans stockage, rien n’est gardé et rien n’échoue', async () => {
    setOfflineBackend(null)
    stubFiles()

    await rememberDocument(ALICE, fakeDoc(4))

    expect(await getOfflineDocument(ALICE, 4)).toBeNull()
    expect(await listOfflineDocuments(ALICE)).toEqual([])
  })
})

describe('toEvict', () => {
  const entry = (key: string, openedAt: number, size = 1) => ({ key, openedAt, size })

  it('garde les plus récents dans la limite du nombre de documents', () => {
    const entries = [entry('a', 1), entry('b', 3), entry('c', 2)]
    expect(toEvict(entries, { count: 2, bytes: 100 })).toEqual(['a'])
  })

  it('garde les plus récents dans la limite de taille, sans écarter un petit document plus ancien', () => {
    const entries = [entry('a', 3, 60), entry('b', 2, 50), entry('c', 1, 30)]
    expect(toEvict(entries, { count: 10, bytes: 100 })).toEqual(['b'])
  })

  it('n’écarte rien sous les limites', () => {
    expect(toEvict([entry('a', 1), entry('b', 2)], { count: 20, bytes: 100 })).toEqual([])
  })
})

describe('rememberDocument', () => {
  beforeEach(() => setOfflineBackend(memoryBackend()))
  afterEach(() => {
    setOfflineBackend(null)
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('fait sortir les plus anciens au-delà de 20 documents', async () => {
    stubFiles()
    vi.useFakeTimers()
    for (let id = 1; id <= 21; id++) {
      vi.setSystemTime(new Date(Date.UTC(2026, 10, 22, 10, id)))
      await rememberDocument(ALICE, fakeDoc(id))
    }

    const ids = (await listOfflineDocuments(ALICE)).map((d) => d.document.id)
    expect(ids).toHaveLength(20)
    expect(ids).not.toContain(1)
    expect(await getOfflineFile(1, 'apercu')).toBeNull()
  })
})
