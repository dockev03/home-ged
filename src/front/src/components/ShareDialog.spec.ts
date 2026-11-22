import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ShareDialog from './ShareDialog.vue'

interface Call {
  method: string
  path: string
  body: unknown
}

const CREATED = {
  id: 'abcdefghijklmnopqrstuv',
  document: 5,
  titre: null,
  creeLe: '2026-11-22T12:00:00Z',
  expireLe: '2026-11-29T12:00:00Z',
  telechargementsMax: 3,
  telechargements: 0,
}

function stubBff(joursMax = 30, failure?: Response) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const method = init?.method ?? 'GET'
      calls.push({ method, path: url.pathname, body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined })
      if (url.pathname === '/api/partages/reglages') return Response.json({ joursMax })
      if (failure) return failure.clone()
      return Response.json(CREATED, { status: 201 })
    }),
  )
  return calls
}

async function mountDialog() {
  const wrapper = mount(ShareDialog, {
    props: { open: true, documentId: 5, documentTitle: 'Facture EDF' },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

type Wrapper = Awaited<ReturnType<typeof mountDialog>>

const button = (wrapper: Wrapper, label: string) => {
  const found = wrapper.findAll('button').find((b) => b.text() === label)
  expect(found, `bouton « ${label} »`).toBeDefined()
  return found!
}

const posts = (calls: Call[]) => calls.filter((c) => c.method === 'POST')

describe('ShareDialog', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-22T12:00:00Z'))
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('propose les durées autorisées, 7 jours par défaut, avec la date d’expiration', async () => {
    stubBff(10)
    const wrapper = await mountDialog()

    const options = wrapper.findAll('#partage-duree option').map((o) => o.text())
    expect(options).toEqual(['1 jour', '7 jours', '10 jours'])
    expect((wrapper.find('#partage-duree').element as HTMLSelectElement).value).toBe('7')
    expect(wrapper.text()).toContain('expirera le 29 novembre 2026')
    expect(wrapper.text()).toContain('Facture EDF')
  })

  it('refuse un mot de passe trop court sans appeler le BFF', async () => {
    const calls = stubBff()
    const wrapper = await mountDialog()

    await wrapper.find('#partage-mdp').setValue('court')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('au moins 8 caractères')
    expect(posts(calls)).toHaveLength(0)
  })

  it('génère un mot de passe et l’affiche en clair', async () => {
    stubBff()
    const wrapper = await mountDialog()

    await button(wrapper, 'Générer').trigger('click')

    const input = wrapper.find('#partage-mdp').element as HTMLInputElement
    expect(input.type).toBe('text')
    expect(input.value).toMatch(/^[A-Za-z2-9]{16}$/)
  })

  it('crée le lien avec durée et limite, puis affiche l’adresse sans le mot de passe', async () => {
    const calls = stubBff()
    const wrapper = await mountDialog()

    await wrapper.find('#partage-mdp').setValue('Correct-Cheval-42')
    await wrapper.find('#partage-duree').setValue('14')
    await wrapper.find('#partage-limite').setValue(true)
    await wrapper.find('#partage-max').setValue(3)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(posts(calls)).toEqual([
      { method: 'POST', path: '/api/partages', body: { document: 5, motDePasse: 'Correct-Cheval-42', jours: 14, telechargementsMax: 3 } },
    ])
    const link = wrapper.find('#partage-lien').element as HTMLInputElement
    expect(link.value).toBe(`${window.location.origin}/s/abcdefghijklmnopqrstuv`)
    expect(wrapper.text()).not.toContain('Correct-Cheval-42')
    expect(wrapper.text()).toContain('3 téléchargements au plus')
  })

  it('sans limite, envoie null', async () => {
    const calls = stubBff()
    const wrapper = await mountDialog()

    await wrapper.find('#partage-mdp').setValue('Correct-Cheval-42')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(posts(calls)[0]!.body).toMatchObject({ jours: 7, telechargementsMax: null })
  })

  it('copie le lien dans le presse-papiers', async () => {
    stubBff()
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    const wrapper = await mountDialog()

    await wrapper.find('#partage-mdp').setValue('Correct-Cheval-42')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    await button(wrapper, 'Copier le lien').trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/s/abcdefghijklmnopqrstuv`)
    expect(wrapper.text()).toContain('Lien copié.')
  })

  it('affiche le refus du BFF (document non accessible)', async () => {
    stubBff(30, Response.json({ title: 'Document introuvable.', code: 'introuvable' }, { status: 404 }))
    const wrapper = await mountDialog()

    await wrapper.find('#partage-mdp').setValue('Correct-Cheval-42')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toBe('Document introuvable.')
    expect(wrapper.find('#partage-lien').exists()).toBe(false)
  })
})
