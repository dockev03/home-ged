import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * État de la connexion de l'appareil (navigator.onLine et événements online / offline). Le
 * navigateur peut se croire en ligne sans l'être : les écrans traitent aussi les erreurs réseau.
 */
export const useNetworkStore = defineStore('network', () => {
  const online = ref(typeof navigator === 'undefined' ? true : navigator.onLine)
  const reconnectHandlers = new Set<() => void>()

  window.addEventListener('online', () => {
    online.value = true
    for (const handler of reconnectHandlers) handler()
  })
  window.addEventListener('offline', () => {
    online.value = false
  })

  /** Appelé à chaque retour du réseau ; renvoie de quoi se désabonner. */
  function onReconnect(handler: () => void) {
    reconnectHandlers.add(handler)
    return () => reconnectHandlers.delete(handler)
  }

  return { online, onReconnect }
})
