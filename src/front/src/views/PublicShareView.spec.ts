import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import PublicShareView from './PublicShareView.vue'

const ID = 'AAAAAAAAAAAAAAAAAAAAAA'
const BASE = `/api/public/partages/${ID}`

const SHARE = { titre: 'Bail', expireLe: '2026-11-29T08:00:00Z', telechargementsRestants: 2 }

const problem = (status: number, code: string, title: string) => Response.json({ title, code }, { status })

interface Call {
  method: string
  path: string
  body: unknown
  headers: Record<string, string>
}

/** Faux BFF : `info` répond au GET (ticket), `access` au POST du mot de passe. */
function stubBff(info: () => Response, access: (password: string) => Response = () => Response.json(SHARE)) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      const method = init?.method ?? 'GET'
      const body = init?.body ? (JSON.parse(String(init.body)) as { motDePasse: string }) : undefined
      calls.push({ method, path: url.pathname, body, headers: (init?.headers ?? {}) as Record<string, string> })
      if (method === 'POST') return access(body!.motDePasse)
      return info()
    }),
  )
  return calls
}

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/s/:id', component: PublicShareView }],
  })
  await router.push(`/s/${ID}`)
  const wrapper = mount(PublicShareView, { global: { plugins: [router] }, attachTo: document.body })
  await flushPromises()
  return wrapper
}

type Wrapper = Awaited<ReturnType<typeof mountView>>

async function submit(wrapper: Wrapper, password: string) {
  await wrapper.find('#mot-de-passe').setValue(password)
  await wrapper.find('form').trigger('submit')
  await flushPromises()
}

describe('PublicShareView', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('demande le mot de passe puis affiche l’aperçu et le téléchargement', async () => {
    const calls = stubBff(() => problem(401, 'mot_de_passe_requis', 'Saisissez le mot de passe du lien.'))
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Document protégé')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)

    await submit(wrapper, 'Correct-Cheval-42')

    const post = calls.find((c) => c.method === 'POST')!
    expect(post.path).toBe(`${BASE}/acces`)
    expect(post.body).toEqual({ motDePasse: 'Correct-Cheval-42' })
    expect(post.headers['X-Requested-With']).toBe('HomeGed')
    expect(wrapper.find('h1').text()).toBe('Bail')
    expect(wrapper.text()).toContain('2 téléchargements restants.')
    expect(wrapper.find('iframe').attributes('src')).toBe(`${BASE}/apercu`)
    const download = wrapper.findAll('a').find((a) => a.text() === 'Télécharger')!
    expect(download.attributes('href')).toBe(`${BASE}/telechargement`)
    // Ni le mot de passe ni un jeton dans les adresses.
    expect(wrapper.html()).not.toContain('Correct-Cheval-42')
  })

  it('rouvre directement le document si le ticket est encore valable', async () => {
    const calls = stubBff(() => Response.json(SHARE))
    const wrapper = await mountView()

    expect(calls).toHaveLength(1)
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find('h1').text()).toBe('Bail')
  })

  it('affiche le refus générique et laisse réessayer', async () => {
    stubBff(
      () => problem(401, 'mot_de_passe_requis', 'Saisissez le mot de passe du lien.'),
      () => problem(404, 'lien_indisponible', 'Lien indisponible ou mot de passe incorrect.'),
    )
    const wrapper = await mountView()

    await submit(wrapper, 'mauvais')

    expect(wrapper.find('[role="alert"]').text()).toBe('Lien indisponible ou mot de passe incorrect.')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('affiche le blocage après trop de tentatives', async () => {
    stubBff(
      () => problem(401, 'mot_de_passe_requis', 'Saisissez le mot de passe du lien.'),
      () => problem(429, 'trop_de_tentatives', 'Trop de tentatives, réessayez dans 15 minutes.'),
    )
    const wrapper = await mountView()

    await submit(wrapper, 'encore')

    expect(wrapper.find('[role="alert"]').text()).toContain('réessayez dans 15 minutes')
  })

  it('n’envoie pas un mot de passe vide', async () => {
    const calls = stubBff(() => problem(401, 'mot_de_passe_requis', 'Saisissez le mot de passe du lien.'))
    const wrapper = await mountView()

    await submit(wrapper, '')

    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0)
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('indique un lien devenu indisponible depuis l’ouverture', async () => {
    stubBff(() => problem(404, 'lien_indisponible', 'Lien indisponible ou mot de passe incorrect.'))
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Ce lien n’est plus disponible.')
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('retire le téléchargement une fois le dernier utilisé', async () => {
    stubBff(() => Response.json({ ...SHARE, telechargementsRestants: 1 }))
    const wrapper = await mountView()

    const download = wrapper.findAll('a').find((a) => a.text() === 'Télécharger')!
    download.element.addEventListener('click', (e) => e.preventDefault())
    await download.trigger('click')

    expect(wrapper.findAll('a').some((a) => a.text() === 'Télécharger')).toBe(false)
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.text()).toContain('Plus aucun téléchargement possible avec ce lien.')
  })

  it('sans limite, n’affiche pas de compteur', async () => {
    stubBff(() => Response.json({ ...SHARE, telechargementsRestants: null }))
    const wrapper = await mountView()

    expect(wrapper.text()).not.toContain('restant')
    expect(wrapper.text()).toContain('Lien valable jusqu’au')
  })
})
