import { describe, expect, it } from 'vitest'
import { formatDate, formatRelative } from './dates'

const now = new Date('2026-11-22T12:00:00Z')
const ago = (seconds: number) => new Date(now.getTime() - seconds * 1000)

describe('formatRelative', () => {
  it.each([
    [ago(20), "à l'instant"],
    [ago(5 * 60), 'il y a 5 minutes'],
    [ago(3 * 3600), 'il y a 3 heures'],
    [ago(24 * 3600), 'hier'],
    [ago(15 * 24 * 3600), 'il y a 2 semaines'],
    [new Date(now.getTime() + 2 * 24 * 3600 * 1000), 'après-demain'],
  ])('%s → %s', (date, expected) => {
    expect(formatRelative(date, now)).toBe(expected)
  })
})

describe('formatDate', () => {
  it('écrit la date en toutes lettres', () => {
    expect(formatDate('2026-12-22T10:00:00Z')).toBe('22 décembre 2026')
  })
})
