import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

/** Choix du thème (PWA-03) : suivre le système par défaut. */
export type ThemeChoice = 'systeme' | 'clair' | 'sombre'

export const THEME_STORAGE_KEY = 'homeged.theme'
const CHOICES: readonly ThemeChoice[] = ['systeme', 'clair', 'sombre']

/** Lecture tolérante : stockage bloqué (navigation privée…) ou valeur inconnue → système. */
function readTheme(): ThemeChoice {
  try {
    const value = localStorage.getItem(THEME_STORAGE_KEY)
    return CHOICES.includes(value as ThemeChoice) ? (value as ThemeChoice) : 'systeme'
  } catch {
    return 'systeme'
  }
}

function writeTheme(choice: ThemeChoice) {
  try {
    if (choice === 'systeme') localStorage.removeItem(THEME_STORAGE_KEY)
    else localStorage.setItem(THEME_STORAGE_KEY, choice)
  } catch {
    // Pas de stockage : le choix vaut pour la visite en cours.
  }
}

/** Préférences propres à cet appareil, mémorisées dans le navigateur. */
export const usePreferencesStore = defineStore('preferences', () => {
  const theme = ref<ThemeChoice>(readTheme())
  watch(theme, writeTheme)
  return { theme }
})
