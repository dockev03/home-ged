/** Géométrie du recadrage (SCAN-02, 03) : fonctions pures, partagées par l'écran et le worker. */

export interface Point {
  x: number
  y: number
}

/** Coins d'une page, toujours dans l'ordre : haut gauche, haut droit, bas droit, bas gauche. */
export type Quad = [Point, Point, Point, Point]

export const CORNER_LABELS = ['en haut à gauche', 'en haut à droite', 'en bas à droite', 'en bas à gauche'] as const

/** Côté le plus long des images confiées au worker : 12 Mpx suffisent largement à un A4 lisible. */
export const MAX_SOURCE_SIDE = 4000

/** Côté le plus long d'une page redressée : environ 300 ppp pour un A4, PDF raisonnable. */
export const MAX_PAGE_SIDE = 2500

const distance = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.y - b.y)

/**
 * Remet 4 points dans l'ordre haut gauche, haut droit, bas droit, bas gauche : le haut gauche a la
 * plus petite somme x + y, le bas droit la plus grande ; le haut droit a le plus grand écart x − y.
 */
export function orderCorners(points: readonly Point[]): Quad {
  if (points.length !== 4) throw new RangeError('4 coins attendus.')
  const bySum = [...points].sort((a, b) => a.x + a.y - (b.x + b.y))
  const topLeft = bySum[0]!
  const bottomRight = bySum[3]!
  const [p, q] = [bySum[1]!, bySum[2]!]
  const [topRight, bottomLeft] = p.x - p.y >= q.x - q.y ? [p, q] : [q, p]
  return [topLeft, topRight, bottomRight, bottomLeft]
}

/** Toute l'image : choix par défaut quand aucune page n'est détectée. */
export function fullFrame(width: number, height: number): Quad {
  return [
    { x: 0, y: 0 },
    { x: width, y: 0 },
    { x: width, y: height },
    { x: 0, y: height },
  ]
}

export function clampPoint(point: Point, width: number, height: number): Point {
  return { x: Math.min(Math.max(point.x, 0), width), y: Math.min(Math.max(point.y, 0), height) }
}

export function scaleQuad(quad: Quad, factor: number): Quad {
  return quad.map((p) => ({ x: p.x * factor, y: p.y * factor })) as Quad
}

/**
 * Rentre chaque coin vers le centre pour que les bords s'écartent d'environ `margin` : le contour
 * détecté longe l'extérieur du trait de bord (dilaté), la page garderait sinon un liseré de fond.
 */
export function insetQuad(quad: Quad, margin: number): Quad {
  const cx = quad.reduce((sum, p) => sum + p.x, 0) / 4
  const cy = quad.reduce((sum, p) => sum + p.y, 0) / 4
  return quad.map((p) => {
    const length = Math.hypot(cx - p.x, cy - p.y)
    // Le long de la diagonale, rentrer de margin × √2 décale les deux bords voisins de margin.
    const step = Math.min(margin * Math.SQRT2, length / 2)
    return length ? { x: p.x + ((cx - p.x) / length) * step, y: p.y + ((cy - p.y) / length) * step } : p
  }) as Quad
}

/** Aire du quadrilatère (formule du lacet). */
export function quadArea(quad: Quad): number {
  let sum = 0
  for (let i = 0; i < 4; i++) {
    const a = quad[i]!
    const b = quad[(i + 1) % 4]!
    sum += a.x * b.y - b.x * a.y
  }
  return Math.abs(sum) / 2
}

/** Vrai si les 4 coins forment un quadrilatère convexe non aplati (recadrage possible). */
export function isConvexQuad(quad: Quad): boolean {
  let sign = 0
  for (let i = 0; i < 4; i++) {
    const a = quad[i]!
    const b = quad[(i + 1) % 4]!
    const c = quad[(i + 2) % 4]!
    const cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)
    if (cross === 0) return false
    if (sign === 0) sign = Math.sign(cross)
    else if (Math.sign(cross) !== sign) return false
  }
  return true
}

/** Dimensions réduites pour que le plus grand côté ne dépasse pas `maxSide` (jamais agrandies). */
export function fitWithin(width: number, height: number, maxSide: number): { width: number; height: number; scale: number } {
  const scale = Math.min(1, maxSide / Math.max(width, height))
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)), scale }
}

/** Taille de la page redressée : la plus grande largeur et la plus grande hauteur du quadrilatère. */
export function pageSize(quad: Quad, maxSide = MAX_PAGE_SIDE): { width: number; height: number } {
  const [tl, tr, br, bl] = quad
  const width = Math.max(distance(tl, tr), distance(bl, br))
  const height = Math.max(distance(tl, bl), distance(tr, br))
  const fitted = fitWithin(width, height, maxSide)
  return { width: fitted.width, height: fitted.height }
}
