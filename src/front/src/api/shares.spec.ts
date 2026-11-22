import { describe, expect, it } from 'vitest'
import { durationChoices, generatePassword, isExhausted, shareUrl, type ShareLink } from './shares'

const link = (telechargements: number, telechargementsMax: number | null): ShareLink => ({
  id: 'AAAAAAAAAAAAAAAAAAAAAA',
  document: 5,
  titre: 'Bail',
  creeLe: '2026-11-22T08:00:00Z',
  expireLe: '2026-11-29T08:00:00Z',
  telechargementsMax,
  telechargements,
})

describe('liens de partage', () => {
  it("construit l'adresse publique du lien", () => {
    expect(shareUrl('abc_DEF-123', 'https://ged.example')).toBe('https://ged.example/s/abc_DEF-123')
  })

  it('propose des durées bornées par le maximum, maximum compris', () => {
    expect(durationChoices(30)).toEqual([1, 7, 14, 30])
    expect(durationChoices(10)).toEqual([1, 7, 10])
    expect(durationChoices(1)).toEqual([1])
    expect(durationChoices(365)).toEqual([1, 7, 14, 30, 90, 365])
  })

  it('repère un lien dont les téléchargements sont épuisés', () => {
    expect(isExhausted(link(3, 3))).toBe(true)
    expect(isExhausted(link(2, 3))).toBe(false)
    expect(isExhausted(link(50, null))).toBe(false)
  })

  it('génère des mots de passe lisibles, de la longueur demandée et tous différents', () => {
    const passwords = Array.from({ length: 50 }, () => generatePassword())
    for (const p of passwords) expect(p).toMatch(/^[A-HJKMNP-Za-hjkmnp-z2-9]{16}$/)
    expect(new Set(passwords).size).toBe(50)
    expect(generatePassword(24)).toHaveLength(24)
  })
})
