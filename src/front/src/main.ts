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

// Session expirée ou révoquée pendant l'utilisation : retour à l'écran de connexion.
onUnauthorized(() => {
  useAuthStore(pinia).clear()
  const current = router.currentRoute.value
  if (!current.meta.public) void router.push(loginRoute(current.fullPath))
})
