import type { ThemeChoice } from '../stores/preferences'

/** Couleurs de la barre d'état mobile = fond de la barre du haut (bg-body-tertiary de Bootstrap). */
export const THEME_COLORS = { light: '#f8f9fa', dark: '#2b3035' } as const

/** Thème réellement affiché : le choix de l'utilisateur, ou celui du système. */
export function isDark(choice: ThemeChoice, systemDark: boolean): boolean {
  return choice === 'systeme' ? systemDark : choice === 'sombre'
}

/**
 * Aligne la page sur le thème affiché : data-bs-theme (Bootstrap), color-scheme (barres de
 * défilement, champs natifs) et theme-color (barre d'état). Les deux balises theme-color
 * d'index.html, une par schéma système, reçoivent la même couleur quand le thème est imposé.
 */
export function applyBrowserTheme(choice: ThemeChoice, systemDark: boolean, doc: Document = document) {
  const root = doc.documentElement
  root.dataset.bsTheme = isDark(choice, systemDark) ? 'dark' : 'light'
  root.style.colorScheme = choice === 'systeme' ? '' : choice === 'sombre' ? 'dark' : 'light'
  for (const meta of doc.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const systemDarkMeta = (meta.getAttribute('media') ?? '').includes('dark')
    meta.content = isDark(choice, systemDarkMeta) ? THEME_COLORS.dark : THEME_COLORS.light
  }
}
