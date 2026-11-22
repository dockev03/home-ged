import { describe, expect, it } from 'vitest'
import { clampPoint, fitWithin, fullFrame, insetQuad, isConvexQuad, orderCorners, pageSize, quadArea, scaleQuad, type Quad } from './geometry'

const square: Quad = [
  { x: 10, y: 10 },
  { x: 110, y: 10 },
  { x: 110, y: 210 },
  { x: 10, y: 210 },
]

describe('géométrie du recadrage', () => {
  it('remet les coins dans l’ordre quel que soit l’ordre reçu', () => {
    expect(orderCorners([square[2], square[0], square[3], square[1]])).toEqual(square)
  })

  it('ordonne une page photographiée de biais', () => {
    const tilted = [
      { x: 30, y: 5 },
      { x: 400, y: 60 },
      { x: 360, y: 560 },
      { x: 0, y: 500 },
    ]
    expect(orderCorners([tilted[3]!, tilted[1]!, tilted[0]!, tilted[2]!])).toEqual(tilted)
  })

  it('refuse un nombre de coins autre que 4', () => {
    expect(() => orderCorners(square.slice(0, 3))).toThrow(RangeError)
  })

  it('cadre toute l’image par défaut', () => {
    expect(fullFrame(100, 50)).toEqual([
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 50 },
      { x: 0, y: 50 },
    ])
  })

  it('garde un coin dans l’image', () => {
    expect(clampPoint({ x: -5, y: 80 }, 100, 50)).toEqual({ x: 0, y: 50 })
  })

  it('change l’échelle des coins', () => {
    expect(scaleQuad(square, 2)[2]).toEqual({ x: 220, y: 420 })
  })

  it('rentre les coins pour écarter les bords', () => {
    const inset = insetQuad(fullFrame(100, 100), 2)
    expect(inset[0].x).toBeCloseTo(2)
    expect(inset[0].y).toBeCloseTo(2)
    expect(inset[2].x).toBeCloseTo(98)
    expect(inset[2].y).toBeCloseTo(98)
    // Jamais au-delà du centre, même avec une marge démesurée.
    expect(insetQuad(fullFrame(10, 10), 100)[0].x).toBeLessThan(5)
  })

  it('calcule l’aire', () => {
    expect(quadArea(square)).toBe(20000)
  })

  it('reconnaît un quadrilatère convexe', () => {
    expect(isConvexQuad(square)).toBe(true)
    // Coins croisés (haut droit et bas droit inversés).
    expect(isConvexQuad([square[0], square[2], square[1], square[3]])).toBe(false)
    // Coin rentrant.
    expect(isConvexQuad([square[0], { x: 50, y: 150 }, square[2], square[3]])).toBe(false)
    // Trois coins alignés.
    expect(isConvexQuad([square[0], { x: 60, y: 10 }, square[1], square[3]])).toBe(false)
  })

  it('réduit sans jamais agrandir', () => {
    expect(fitWithin(8000, 6000, 4000)).toEqual({ width: 4000, height: 3000, scale: 0.5 })
    expect(fitWithin(800, 600, 4000)).toEqual({ width: 800, height: 600, scale: 1 })
  })

  it('donne la taille de la page redressée, plafonnée', () => {
    expect(pageSize(square)).toEqual({ width: 100, height: 200 })
    expect(pageSize(scaleQuad(square, 50))).toEqual({ width: 1250, height: 2500 })
  })
})
