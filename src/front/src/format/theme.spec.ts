import { beforeEach, describe, expect, it } from 'vitest'
import { applyBrowserTheme, THEME_COLORS } from './theme'

const metas = () => [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map((m) => m.content)
const root = () => document.documentElement

describe('applyBrowserTheme', () => {
  beforeEach(() => {
    document.head.innerHTML = `
      <meta name="theme-color" content="#f8f9fa" media="(prefers-color-scheme: light)" />
      <meta name="theme-color" content="#2b3035" media="(prefers-color-scheme: dark)" />`
    root().style.colorScheme = ''
    delete root().dataset.bsTheme
  })

  it('impose le sombre à Bootstrap, à la barre d’état et aux contrôles natifs', () => {
    applyBrowserTheme('sombre', false)
    expect(root().dataset.bsTheme).toBe('dark')
    expect(metas()).toEqual([THEME_COLORS.dark, THEME_COLORS.dark])
    expect(root().style.colorScheme).toBe('dark')
  })

  it('impose le clair', () => {
    applyBrowserTheme('clair', true)
    expect(root().dataset.bsTheme).toBe('light')
    expect(metas()).toEqual([THEME_COLORS.light, THEME_COLORS.light])
    expect(root().style.colorScheme).toBe('light')
  })

  it('rend la main au système', () => {
    applyBrowserTheme('sombre', false)
    applyBrowserTheme('systeme', true)
    expect(root().dataset.bsTheme).toBe('dark')
    expect(metas()).toEqual([THEME_COLORS.light, THEME_COLORS.dark])
    expect(root().style.colorScheme).toBe('')
  })
})
