import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import LoginView from './LoginView.vue'

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ status, title, code }), { status })

async function mountLogin(path = '/connexion') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/connexion', component: LoginView },
      { path: '/:p(.*)*', component: { template: '<p>ailleurs</p>' } },
    ],
  })
  await router.push(path)
  const wrapper = mount(LoginView, { global: { plugins: [router] }, attachTo: document.body })
  return { wrapper, router }
}

async function fill(wrapper: Awaited<ReturnType<typeof mountLogin>>['wrapper'], name: string, value: string) {
  await wrapper.find(`input[name="${name}"]`).setValue(value)
}

describe('LoginView', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('demande le code de vérification si Paperless le requiert, puis renvoie les trois valeurs', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problem(401, 'Saisissez le code de vérification.', 'mfa_requis'))
      .mockResolvedValueOnce(new Response('{"id":2,"username":"bob"}'))
    vi.stubGlobal('fetch', fetchMock)
    const { wrapper, router } = await mountLogin('/connexion?suite=/documents')

    expect(wrapper.find('input[name="code"]').exists()).toBe(false)
    await fill(wrapper, 'username', 'bob')
    await fill(wrapper, 'password', 'secret')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('input[name="code"]').exists()).toBe(true)
    expect(wrapper.text()).toContain("application d'authentification")

    await fill(wrapper, 'code', '123456')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const [, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(JSON.parse(init.body as string)).toEqual({ username: 'bob', password: 'secret', code: '123456' })
    expect(router.currentRoute.value.path).toBe('/documents')
  })

  it("affiche l'erreur d'identifiants sans dévoiler le champ code", async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(401, 'x', 'identifiants_invalides')))
    const { wrapper } = await mountLogin()

    await fill(wrapper, 'username', 'alice')
    await fill(wrapper, 'password', 'faux')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toBe('Identifiant ou mot de passe incorrect.')
    expect(wrapper.find('input[name="code"]').exists()).toBe(false)
  })

  it('affiche le message de blocage fourni par le BFF', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(problem(429, 'Trop de tentatives, réessayez dans 15 minutes.', 'trop_de_tentatives')),
    )
    const { wrapper } = await mountLogin()

    await fill(wrapper, 'username', 'alice')
    await fill(wrapper, 'password', 'faux')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toBe('Trop de tentatives, réessayez dans 15 minutes.')
  })

  it('signale un BFF injoignable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('réseau')))
    const { wrapper } = await mountLogin()

    await fill(wrapper, 'username', 'alice')
    await fill(wrapper, 'password', 'secret')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('Impossible de joindre Home-GED')
  })
})
