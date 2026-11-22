import { beforeEach, describe, expect, it } from 'vitest'
import { applyBrowserTheme, THEME_COLORS } from './theme'

const metas = () => [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map((m) => m.content)

describe('applyBrowserTheme', () => {
  beforeEach(() => {
    document.head.innerHTML = `
      <meta name="theme-color" content="#f7f7f8" media="(prefers-color-scheme: light)" />
      <meta name="theme-color" content="#18181c" media="(prefers-color-scheme: dark)" />`
    document.documentElement.style.colorScheme = ''
  })

  it('impose le sombre à la barre d’état et aux contrôles natifs', () => {
    applyBrowserTheme('sombre')
    expect(metas()).toEqual([THEME_COLORS.dark, THEME_COLORS.dark])
    expect(document.documentElement.style.colorScheme).toBe('dark')
  })

  it('impose le clair', () => {
    applyBrowserTheme('clair')
    expect(metas()).toEqual([THEME_COLORS.light, THEME_COLORS.light])
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('rend la main au système', () => {
    applyBrowserTheme('sombre')
    applyBrowserTheme('systeme')
    expect(metas()).toEqual([THEME_COLORS.light, THEME_COLORS.dark])
    expect(document.documentElement.style.colorScheme).toBe('')
  })
})
