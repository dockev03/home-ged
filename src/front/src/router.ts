import { createRouter, createWebHistory } from 'vue-router'

export const routes = [
  { path: '/', name: 'accueil', component: () => import('./views/HomeView.vue') },
  { path: '/:chemin(.*)*', name: 'introuvable', component: () => import('./views/NotFoundView.vue') },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})
