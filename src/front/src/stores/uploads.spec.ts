import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MAX_UPLOAD_BYTES } from '../api/documents'
import { memoryBackend, offlineDb, setOfflineBackend } from '../offline/db'
import { POLL_MS, RETRY_MS, useUploadsStore } from './uploads'

const TASK = '3f2504e0-4f89-11d3-9a0c-0305e82c3302'

/** Faux XMLHttpRequest : réponse 202 et tâche, ou la réponse donnée ; envoi suspendu si `hold`. */
function stubXhr(options: { status?: number; body?: string; hold?: boolean } = {}) {
  const sent: { url: string; headers: Record<string, string>; form: FormData; xhr: FakeXhr }[] = []
  class FakeXhr {
    upload = { onprogress: null as ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null }
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    onabort: (() => void) | null = null
    status = 0
    responseText = ''
    withCredentials = false
    private url = ''
    private headers: Record<string, string> = {}
    open(_method: string, url: string) {
      this.url = url
    }
    setRequestHeader(name: string, value: string) {
      this.headers[name] = value
    }
    send(form: FormData) {
      sent.push({ url: this.url, headers: this.headers, form, xhr: this })
      this.upload.onprogress?.({ lengthComputable: true, loaded: 25, total: 100 })
      if (!options.hold) this.finish()
    }
    finish() {
      this.status = options.status ?? 202
      this.responseText = options.body ?? `{"tache":"${TASK}"}`
      queueMicrotask(() => this.onload?.())
    }
    abort() {
      queueMicrotask(() => this.onabort?.())
    }
  }
  vi.stubGlobal('XMLHttpRequest', FakeXhr)
  return sent
}

/** Faux BFF pour le suivi de la tâche : états successifs renvoyés à chaque lecture. */
function stubTask(...states: { status: string; message?: string | null; document_id?: number | null }[]) {
  let call = 0
  const fetchMock = vi.fn(async () => {
    const state = states[Math.min(call++, states.length - 1)]!
    return Response.json({ message: null, document_id: null, ...state })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const pdf = (name = 'facture.pdf', content = '%PDF') => new File([content], name, { type: 'application/pdf' })

describe('store uploads', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('joint titre, étiquettes et champs personnalisés à l’envoi (SCAN-06)', async () => {
    const sent = stubXhr()
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()

    uploads.add([pdf('scan.pdf')], { title: '  Facture EDF  ', tags: [3, 7], customFields: { 2: 'opt-kevin' } })
    await flushPromises()

    const form = sent[0]!.form
    expect(form.get('title')).toBe('Facture EDF')
    expect(form.getAll('tags')).toEqual(['3', '7'])
    expect(JSON.parse(form.get('custom_fields') as string)).toEqual({ 2: 'opt-kevin' })
    uploads.reset()
  })

  it('n’ajoute aucun champ de métadonnées vide', async () => {
    const sent = stubXhr()
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()

    uploads.add([pdf()], { title: '   ', tags: [], customFields: {} })
    await flushPromises()

    expect([...sent[0]!.form.keys()]).toEqual(['document'])
    uploads.reset()
  })

  it('envoie le fichier, suit le traitement Paperless et signale le document créé', async () => {
    const sent = stubXhr()
    const fetchMock = stubTask({ status: 'started' }, { status: 'success', document_id: 42 })
    const uploads = useUploadsStore()

    uploads.add([pdf()])
    await flushPromises()

    expect(sent).toHaveLength(1)
    expect(sent[0]!.url).toBe('/api/documents/upload')
    expect(sent[0]!.headers['X-Requested-With']).toBe('HomeGed')
    expect((sent[0]!.form.get('document') as File).name).toBe('facture.pdf')
    expect(uploads.items[0]!.status).toBe('traitement')
    expect(uploads.pending).toBe(true)
    expect(uploads.active).toBe(false)

    await vi.advanceTimersByTimeAsync(POLL_MS)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith(`/api/taches/${TASK}`, expect.anything())
    expect(uploads.items[0]).toMatchObject({ status: 'termine', documentId: 42 })
    expect(uploads.added).toBe(1)
    expect(uploads.pending).toBe(false)
  })

  it('envoie les fichiers un par un, dans l’ordre', async () => {
    const sent = stubXhr({ hold: true })
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()

    uploads.add([pdf('a.pdf'), pdf('b.pdf')])
    await flushPromises()
    expect(sent.map((s) => (s.form.get('document') as File).name)).toEqual(['a.pdf'])
    expect(uploads.items.map((u) => u.status)).toEqual(['envoi', 'attente'])
    expect(uploads.items[0]!.progress).toBe(0.25)

    sent[0]!.xhr.finish()
    await flushPromises()
    expect(sent.map((s) => (s.form.get('document') as File).name)).toEqual(['a.pdf', 'b.pdf'])
  })

  it('refuse sans envoi un fichier vide ou de plus de 200 Mo', async () => {
    const sent = stubXhr()
    const uploads = useUploadsStore()
    const big = pdf('gros.pdf')
    Object.defineProperty(big, 'size', { value: MAX_UPLOAD_BYTES + 1 })

    uploads.add([big, pdf('vide.pdf', '')])
    await flushPromises()

    expect(sent).toHaveLength(0)
    expect(uploads.items.map((u) => [u.status, u.message])).toEqual([
      ['echec', 'Fichier trop volumineux (200 Mo au plus).'],
      ['echec', 'Fichier vide.'],
    ])
  })

  it('affiche le refus du BFF et l’échec du traitement (doublon)', async () => {
    stubXhr({ status: 403, body: '{"title":"Votre compte n\'a pas le droit d\'ajouter des documents.","code":"acces_refuse"}' })
    const uploads = useUploadsStore()
    uploads.add([pdf()])
    await flushPromises()
    expect(uploads.items[0]).toMatchObject({ status: 'echec', message: "Votre compte n'a pas le droit d'ajouter des documents." })

    stubXhr()
    stubTask({ status: 'failure', message: 'Not consuming facture.pdf: It is a duplicate of Facture (#5).' })
    uploads.add([pdf()])
    await flushPromises()
    expect(uploads.items[1]).toMatchObject({ status: 'echec', message: 'Not consuming facture.pdf: It is a duplicate of Facture (#5).' })
    expect(uploads.added).toBe(0)
  })

  it('annule un envoi en cours ou en attente, puis efface les envois finis', async () => {
    stubXhr({ hold: true })
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()
    uploads.add([pdf('a.pdf'), pdf('b.pdf')])
    await flushPromises()

    uploads.cancel(uploads.items[1]!.id)
    uploads.cancel(uploads.items[0]!.id)
    await flushPromises()

    expect(uploads.items.map((u) => u.status)).toEqual(['annule', 'annule'])
    uploads.clearFinished()
    expect(uploads.items).toHaveLength(0)
  })
})

/** Connexion de l'appareil vue par le navigateur, avec l'événement correspondant. */
function setOnline(online: boolean) {
  Object.defineProperty(navigator, 'onLine', { value: online, configurable: true })
  window.dispatchEvent(new Event(online ? 'online' : 'offline'))
}

const stored = async () => (await offlineDb.getAll<{ key: string; name: string; userId: number }>('envois')).map((r) => r.name)

describe('store uploads hors ligne (PWA-10)', () => {
  const ALICE = 1

  beforeEach(() => {
    setOfflineBackend(memoryBackend())
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })
  afterEach(() => {
    setOnline(true)
    setOfflineBackend(null)
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('hors ligne, garde le fichier sur l’appareil et l’envoie au retour du réseau', async () => {
    setOnline(false)
    const sent = stubXhr()
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()
    await uploads.restore(ALICE)

    uploads.add([pdf('scan.pdf')], { title: 'Facture' })
    await flushPromises()

    expect(sent).toHaveLength(0)
    expect(uploads.items[0]).toMatchObject({ status: 'reseau' })
    expect(uploads.waiting).toBe(1)
    expect(uploads.active).toBe(false)
    expect(await stored()).toEqual(['scan.pdf'])

    setOnline(true)
    await flushPromises()

    expect(sent).toHaveLength(1)
    expect(sent[0]!.form.get('title')).toBe('Facture')
    expect(uploads.items[0]!.status).toBe('traitement')
    expect(await stored()).toEqual([])
    uploads.reset()
  })

  it('reprend après une coupure pendant l’envoi, puis au délai de nouvel essai', async () => {
    const sent = stubXhr({ hold: true })
    stubTask({ status: 'started' })
    const uploads = useUploadsStore()
    await uploads.restore(ALICE)
    uploads.add([pdf()])
    await flushPromises()

    sent[0]!.xhr.onerror?.()
    await flushPromises()
    expect(uploads.items[0]).toMatchObject({ status: 'reseau', progress: 0 })
    expect(await stored()).toEqual(['facture.pdf'])

    await vi.advanceTimersByTimeAsync(RETRY_MS)
    expect(sent).toHaveLength(2)
    sent[1]!.xhr.finish()
    await flushPromises()
    expect(uploads.items[0]!.status).toBe('traitement')
    expect(await stored()).toEqual([])
    uploads.reset()
  })

  it('retrouve les envois gardés à la réouverture de l’application', async () => {
    setOnline(false)
    stubXhr()
    stubTask({ status: 'started' })
    const before = useUploadsStore()
    await before.restore(ALICE)
    before.add([pdf('a.pdf'), pdf('b.pdf')])
    await flushPromises()
    before.reset()

    // Application rouverte, toujours hors ligne : les fichiers attendent toujours.
    setActivePinia(createPinia())
    const after = useUploadsStore()
    await after.restore(ALICE)
    expect(after.items.map((u) => [u.name, u.status])).toEqual([
      ['a.pdf', 'reseau'],
      ['b.pdf', 'reseau'],
    ])

    // Un autre utilisateur ne les voit pas.
    setActivePinia(createPinia())
    const other = useUploadsStore()
    await other.restore(2)
    expect(other.items).toEqual([])
  })

  it('session expirée : l’envoi attend la reconnexion, sans être perdu', async () => {
    stubXhr({ status: 401, body: '{"title":"Session expirée."}' })
    const uploads = useUploadsStore()
    await uploads.restore(ALICE)

    uploads.add([pdf()])
    await flushPromises()

    expect(uploads.items[0]).toMatchObject({ status: 'reseau', message: 'Session expirée : l’envoi partira après reconnexion.' })
    expect(await stored()).toEqual(['facture.pdf'])
    uploads.reset()
    expect(await stored()).toEqual(['facture.pdf'])
  })

  it('un refus du BFF ou une annulation retire le fichier de l’appareil', async () => {
    stubXhr({ status: 403, body: '{"title":"Refusé."}' })
    const uploads = useUploadsStore()
    await uploads.restore(ALICE)
    uploads.add([pdf('refuse.pdf')])
    await flushPromises()
    expect(uploads.items[0]!.status).toBe('echec')

    setOnline(false)
    uploads.add([pdf('annule.pdf')])
    await flushPromises()
    expect(await stored()).toEqual(['annule.pdf'])
    uploads.cancel(uploads.items[1]!.id)
    await flushPromises()

    expect(uploads.items[1]!.status).toBe('annule')
    expect(await stored()).toEqual([])
  })
})
