import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as api from '../api/auth'
import { useOpenDocumentsStore } from './openDocuments'
import { useReferencesStore } from './references'
import { useSavedViewsStore } from './savedViews'
import { useUploadsStore } from './uploads'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<api.CurrentUser | null>(null)
  let loading: Promise<void> | null = null

  const isAuthenticated = computed(() => user.value !== null)

  /** Vérifie la session une fois (au premier affichage) ; les appels suivants réutilisent le résultat. */
  function ensureLoaded(): Promise<void> {
    loading ??= api.getCurrentUser().then(
      (current) => {
        user.value = current
      },
      (error: unknown) => {
        loading = null
        throw error
      },
    )
    return loading
  }

  async function login(credentials: api.Credentials) {
    user.value = await api.login(credentials)
    loading = Promise.resolve()
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      clear()
    }
  }

  /** Oublie l'utilisateur sans appeler le BFF (session expirée ou révoquée). */
  function clear() {
    user.value = null
    useReferencesStore().reset()
    useSavedViewsStore().reset()
    useUploadsStore().reset()
    useOpenDocumentsStore().reset()
    loading = Promise.resolve()
  }

  return { user, isAuthenticated, ensureLoaded, login, logout, clear }
})
