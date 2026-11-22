import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { onUnauthorized } from './api/http'
import { loginRoute, router } from './router'
import { useAuthStore } from './stores/auth'
import 'bootstrap/dist/css/bootstrap.min.css'
import './styles.css'

const pinia = createPinia()
createApp(App).use(pinia).use(router).mount('#app')

function toLogin() {
  const current = router.currentRoute.value
  if (!current.meta.public) void router.push(loginRoute(current.fullPath))
}

// Session expirée ou révoquée pendant l'utilisation : retour à l'écran de connexion. Les envois en
// attente restent sur l'appareil pour partir après la reconnexion (PWA-10).
onUnauthorized(() => {
  useAuthStore(pinia).clear({ sessionExpired: true })
  toLogin()
})
// Session reprise hors ligne, expirée au retour du réseau (PWA-09).
useAuthStore(pinia).onSessionLost(toLogin)
