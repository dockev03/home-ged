import type { ThemeChoice } from '../stores/preferences'

/** Couleurs de la barre d'état mobile (identiques aux balises theme-color d'index.html). */
export const THEME_COLORS = { light: '#f7f7f8', dark: '#18181c' } as const

/**
 * Aligne le navigateur sur le thème affiché : color-scheme (barres de défilement, champs natifs)
 * et theme-color (barre d'état). Les deux balises theme-color d'index.html, une par schéma système,
 * reçoivent la même couleur quand le thème est imposé.
 */
export function applyBrowserTheme(choice: ThemeChoice, doc: Document = document) {
  doc.documentElement.style.colorScheme = choice === 'systeme' ? '' : choice === 'sombre' ? 'dark' : 'light'
  for (const meta of doc.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const systemDark = (meta.getAttribute('media') ?? '').includes('dark')
    const dark = choice === 'systeme' ? systemDark : choice === 'sombre'
    meta.content = dark ? THEME_COLORS.dark : THEME_COLORS.light
  }
}
