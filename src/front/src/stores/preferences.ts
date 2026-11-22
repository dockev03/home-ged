import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

/** Choix du thème (PWA-03) : suivre le système par défaut. */
export type ThemeChoice = 'systeme' | 'clair' | 'sombre'

/** Affichage de la liste des documents (UI-02). */
export type DisplayChoice = 'vignettes' | 'details' | 'tableau'

export const THEME_STORAGE_KEY = 'homeged.theme'
export const DISPLAY_STORAGE_KEY = 'homeged.affichage'
const THEMES: readonly ThemeChoice[] = ['systeme', 'clair', 'sombre']
const DISPLAYS: readonly DisplayChoice[] = ['vignettes', 'details', 'tableau']

/** Lecture tolérante : stockage bloqué (navigation privée…) ou valeur inconnue → valeur par défaut. */
function read<T extends string>(key: string, choices: readonly T[], fallback: T): T {
  try {
    const value = localStorage.getItem(key)
    return choices.includes(value as T) ? (value as T) : fallback
  } catch {
    return fallback
  }
}

/** La valeur par défaut n'est pas stockée : une évolution du défaut s'appliquera. */
function write(key: string, value: string, fallback: string) {
  try {
    if (value === fallback) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    // Pas de stockage : le choix vaut pour la visite en cours.
  }
}

/** Préférences propres à cet appareil, mémorisées dans le navigateur. */
export const usePreferencesStore = defineStore('preferences', () => {
  const theme = ref<ThemeChoice>(read(THEME_STORAGE_KEY, THEMES, 'systeme'))
  const display = ref<DisplayChoice>(read(DISPLAY_STORAGE_KEY, DISPLAYS, 'details'))
  watch(theme, (value) => write(THEME_STORAGE_KEY, value, 'systeme'))
  watch(display, (value) => write(DISPLAY_STORAGE_KEY, value, 'details'))
  return { theme, display }
})
