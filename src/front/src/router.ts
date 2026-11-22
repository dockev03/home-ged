import { createRouter, createWebHistory, type RouteLocationRaw } from 'vue-router'
import { useAuthStore } from './stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** Page accessible sans session. */
    public?: boolean
  }
}

export const routes = [
  { path: '/', name: 'accueil', component: () => import('./views/HomeView.vue') },
  {
    path: '/connexion',
    name: 'connexion',
    component: () => import('./views/LoginView.vue'),
    meta: { public: true },
  },
  { path: '/:chemin(.*)*', name: 'introuvable', component: () => import('./views/NotFoundView.vue') },
]

/** Destination après connexion : chemin interne uniquement (pas de redirection ouverte). */
export function safeRedirect(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : '/'
}

export function loginRoute(currentPath: string): RouteLocationRaw {
  return currentPath === '/' ? { name: 'connexion' } : { name: 'connexion', query: { suite: currentPath } }
}

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  try {
    await auth.ensureLoaded()
  } catch {
    // BFF injoignable : on laisse l'écran de connexion afficher l'erreur.
    return to.meta.public ? true : loginRoute(to.fullPath)
  }
  if (to.name === 'connexion' && auth.isAuthenticated) return safeRedirect(to.query.suite)
  if (!to.meta.public && !auth.isAuthenticated) return loginRoute(to.fullPath)
  return true
})
