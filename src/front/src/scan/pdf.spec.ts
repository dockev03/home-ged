import { describe, expect, it } from 'vitest'
import { A4, buildPdf, JPEG_ERROR, placeOnA4, readJpegInfo } from './pdf'

/** JPEG minimal : SOI, APP0, SOF0 (dimensions, composantes), SOS, données, EOI. */
function jpeg(width: number, height: number, components = 3, sof = 0xc0): Uint8Array {
  const app0 = [0xff, 0xe0, 0x00, 0x10, ...'JFIF'.split('').map((c) => c.charCodeAt(0)), 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]
  const sofLength = 8 + components * 3
  const frame = [0xff, sof, sofLength >> 8, sofLength & 0xff, 8, height >> 8, height & 0xff, width >> 8, width & 0xff, components]
  for (let c = 0; c < components; c++) frame.push(c + 1, 0x11, 0)
  return new Uint8Array([0xff, 0xd8, ...app0, 0xff, 0xff, ...frame, 0xff, 0xda, 0x00, 0x02, 0x12, 0x34, 0x56, 0xff, 0xd9])
}

const latin1 = (bytes: Uint8Array) => new TextDecoder('latin1').decode(bytes)

describe('readJpegInfo', () => {
  it('lit dimensions et composantes après les segments APP et le remplissage', () => {
    expect(readJpegInfo(jpeg(2480, 3508))).toEqual({ width: 2480, height: 3508, components: 3 })
    expect(readJpegInfo(jpeg(800, 600, 1))).toEqual({ width: 800, height: 600, components: 1 })
  })

  it('accepte un JPEG progressif (SOF2)', () => {
    expect(readJpegInfo(jpeg(10, 20, 3, 0xc2))).toEqual({ width: 10, height: 20, components: 3 })
  })

  it('refuse ce qui n’est pas un JPEG ou un JPEG sans en-tête d’image', () => {
    expect(() => readJpegInfo(new TextEncoder().encode('%PDF-1.4'))).toThrow(JPEG_ERROR)
    expect(() => readJpegInfo(new Uint8Array([0xff, 0xd8, 0xff, 0xda, 0x00, 0x02, 0xff, 0xd9]))).toThrow(JPEG_ERROR)
    expect(() => readJpegInfo(jpeg(10, 10).slice(0, 25))).toThrow(JPEG_ERROR)
  })
})

describe('placeOnA4', () => {
  it('centre une page portrait sans la déformer', () => {
    const place = placeOnA4(1000, 2000)
    expect(place.page).toEqual(A4)
    expect(place.height).toBeCloseTo(A4.height)
    expect(place.width).toBeCloseTo(A4.height / 2)
    expect(place.x).toBeCloseTo((A4.width - A4.height / 2) / 2)
    expect(place.y).toBeCloseTo(0)
  })

  it('tourne la page A4 pour une image paysage', () => {
    const place = placeOnA4(3000, 1000)
    expect(place.page).toEqual({ width: A4.height, height: A4.width })
    expect(place.width).toBeCloseTo(A4.height)
  })
})

describe('buildPdf', () => {
  it('refuse un document sans page', () => {
    expect(() => buildPdf([])).toThrow(RangeError)
  })

  it('produit un PDF d’une page A4 par image, dans l’ordre, JPEG intégrés tels quels', () => {
    const portrait = jpeg(2480, 3508)
    const paysage = jpeg(3508, 2480, 1)
    const pdf = buildPdf([portrait, paysage])
    const text = latin1(pdf)

    expect(text.startsWith('%PDF-1.4\n')).toBe(true)
    expect(text.endsWith('%%EOF\n')).toBe(true)
    expect(text).toContain('/Kids [3 0 R 6 0 R] /Count 2')
    expect(text).toContain('/MediaBox [0 0 595.28 841.89]')
    expect(text).toContain('/MediaBox [0 0 841.89 595.28]')
    expect(text).toContain('/Width 2480 /Height 3508 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode')
    expect(text).toContain('/Width 3508 /Height 2480 /ColorSpace /DeviceGray')
    // Chaque JPEG est recopié octet pour octet, dans l'ordre des pages.
    const first = text.indexOf(latin1(portrait))
    const second = text.indexOf(latin1(paysage))
    expect(first).toBeGreaterThan(0)
    expect(second).toBeGreaterThan(first)
    expect(text).toContain(`/Length ${portrait.length} >>\nstream\n`)
  })

  it('a une table xref exacte : chaque entrée pointe sur son objet', () => {
    const pdf = buildPdf([jpeg(100, 100), jpeg(100, 50, 4)])
    const text = latin1(pdf)

    const startxref = Number(/startxref\n(\d+)\n%%EOF\n$/.exec(text)![1])
    expect(text.slice(startxref, startxref + 5)).toBe('xref\n')
    const size = Number(/xref\n0 (\d+)\n/.exec(text.slice(startxref))![1])
    expect(size).toBe(9)
    expect(text).toContain(`/Size ${size} /Root 1 0 R`)

    const entries = text.slice(startxref).split('\n').slice(2, 2 + size)
    expect(entries[0]).toBe('0000000000 65535 f ')
    entries.slice(1).forEach((entry, index) => {
      expect(entry).toMatch(/^\d{10} 00000 n $/)
      const offset = Number(entry.slice(0, 10))
      expect(text.slice(offset).startsWith(`${index + 1} 0 obj\n`)).toBe(true)
    })
    // JPEG CMJN : composantes inversées (Adobe).
    expect(text).toContain('/ColorSpace /DeviceCMYK /BitsPerComponent 8 /Filter /DCTDecode /Decode [1 0 1 0 1 0 1 0]')
  })

  it('place l’image avec la matrice de transformation calculée', () => {
    const text = latin1(buildPdf([jpeg(1000, 2000)]))
    const place = placeOnA4(1000, 2000)
    const round = (v: number) => String(Math.round(v * 100) / 100)
    expect(text).toContain(`q ${round(place.width)} 0 0 ${round(place.height)} ${round(place.x)} 0 cm /Im0 Do Q`)
  })
})
