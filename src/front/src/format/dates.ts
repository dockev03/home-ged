const relative = new Intl.RelativeTimeFormat('fr', { numeric: 'auto' })
const absolute = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'long' })
const absoluteWithTime = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'long', timeStyle: 'short' })

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
]

/** « à l'instant », « il y a 5 minutes », « hier », « dans 3 semaines »… */
export function formatRelative(date: string | Date, now: Date = new Date()): string {
  const seconds = (new Date(date).getTime() - now.getTime()) / 1000
  if (Math.abs(seconds) < 60) return "à l'instant"
  const [unit, size] = UNITS.find(([, s]) => Math.abs(seconds) >= s)!
  return relative.format(Math.round(seconds / size), unit)
}

/** « 22 novembre 2026 ». */
export const formatDate = (date: string | Date) => absolute.format(new Date(date))

/** « 22 novembre 2026 à 09:15 » (info-bulle des dates relatives). */
export const formatDateTime = (date: string | Date) => absoluteWithTime.format(new Date(date))
