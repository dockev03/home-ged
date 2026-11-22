import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MAX_UPLOAD_BYTES } from '../api/documents'
import { POLL_MS, useUploadsStore } from './uploads'

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
