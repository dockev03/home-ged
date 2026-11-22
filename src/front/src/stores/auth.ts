import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as api from '../api/auth'
import { isNetworkError } from '../offline/db'
import { expireSession, forgetEverything, lastUser, rememberUser } from '../offline/session'
import { useNetworkStore } from './network'
import { useOpenDocumentsStore } from './openDocuments'
import { useReferencesStore } from './references'
import { useSavedViewsStore } from './savedViews'
import { useUploadsStore } from './uploads'

/** Hors ligne, nouvelle vérification de la session à ce rythme (en plus du retour du réseau). */
export const REVALIDATE_MS = 30_000

export const useAuthStore = defineStore('auth', () => {
  const user = ref<api.CurrentUser | null>(null)
  /**
   * Vrai quand l'application a démarré sans joindre Home-GED : l'utilisateur est le dernier connecté
   * sur l'appareil et seules les données hors ligne sont disponibles. La session est revérifiée au
   * retour du réseau.
   */
  const offline = ref(false)
  let loading: Promise<void> | null = null
  let revalidateTimer: ReturnType<typeof setInterval> | null = null
  let sessionLost: (() => void) | null = null

  const isAuthenticated = computed(() => user.value !== null)

  /**
   * Session vérifiée par le BFF : l'appareil appartient à cet utilisateur (les données d'un autre
   * compte sont effacées d'abord), puis ses envois gardés reprennent. Le stockage ne retarde pas
   * l'affichage.
   */
  function startSession(current: api.CurrentUser) {
    user.value = current
    setOffline(false)
    const uploads = useUploadsStore()
    void rememberUser(current).then(async () => {
      if (user.value?.id === current.id) await uploads.restore(current.id)
    })
  }

  /** Vérifie la session une fois (au premier affichage) ; les appels suivants réutilisent le résultat. */
  function ensureLoaded(): Promise<void> {
    loading ??= api.getCurrentUser().then(
      (current) => {
        if (current) startSession(current)
        else {
          // Pas de session (expirée pendant que l'application était fermée) : comme un 401.
          user.value = null
          void expireSession()
        }
      },
      async (error: unknown) => {
        // Appareil hors ligne : démarrage avec le dernier utilisateur, sur les données de l'appareil.
        const last = isNetworkError(error) ? await lastUser() : null
        if (last) {
          user.value = last
          setOffline(true)
          void useUploadsStore().restore(last.id)
          return
        }
        loading = null
        throw error
      },
    )
    return loading
  }

  function setOffline(value: boolean) {
    offline.value = value
    if (revalidateTimer !== null) clearInterval(revalidateTimer)
    revalidateTimer = value ? setInterval(() => void revalidate(), REVALIDATE_MS) : null
  }

  /**
   * Hors ligne : nouvel essai de joindre Home-GED. Session toujours valide → retour en ligne ;
   * session expirée entre-temps → fin de session (le gestionnaire de onSessionLost est appelé).
   */
  async function revalidate() {
    if (!offline.value) return
    let current: api.CurrentUser | null
    try {
      current = await api.getCurrentUser()
    } catch {
      return
    }
    if (!offline.value) return
    if (current && current.id === user.value?.id) startSession(current)
    else {
      clear({ sessionExpired: true })
      sessionLost?.()
    }
  }

  /** Appelé quand une session reprise hors ligne s'avère expirée au retour du réseau. */
  function onSessionLost(handler: (() => void) | null) {
    sessionLost = handler
  }

  useNetworkStore().onReconnect(() => void revalidate())

  async function login(credentials: api.Credentials) {
    startSession(await api.login(credentials))
    loading = Promise.resolve()
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      clear()
    }
  }

  /**
   * Oublie l'utilisateur sans appeler le BFF. Déconnexion (par défaut) : rien ne reste sur
   * l'appareil. Session expirée ou révoquée : les envois en attente sont gardés pour la reconnexion.
   */
  function clear(options: { sessionExpired?: boolean } = {}) {
    user.value = null
    setOffline(false)
    useReferencesStore().reset()
    useSavedViewsStore().reset()
    useUploadsStore().reset()
    useOpenDocumentsStore().reset()
    void (options.sessionExpired ? expireSession() : forgetEverything())
    loading = Promise.resolve()
  }

  return { user, offline, isAuthenticated, ensureLoaded, revalidate, onSessionLost, login, logout, clear }
})
