/**
 * Assemblage des pages scannées en un PDF (SCAN-05), entièrement dans le navigateur.
 *
 * Générateur minimal : chaque page JPEG est intégrée telle quelle (filtre DCTDecode, sans
 * recompression ni perte) au centre d'une page A4, dans l'orientation de l'image. Paperless se
 * charge ensuite de l'OCR et produit l'archive PDF/A.
 */

/** Format A4 en points PDF (1/72 de pouce). */
export const A4 = { width: 595.28, height: 841.89 } as const

/** Informations lues dans l'en-tête d'un JPEG. */
export interface JpegInfo {
  width: number
  height: number
  /** 1 (niveaux de gris), 3 (couleur) ou 4 (CMJN). */
  components: 1 | 3 | 4
}

export const JPEG_ERROR = 'Une page n’est pas une image JPEG valide.'

/** Marqueurs SOF (début d'image) : tous de 0xC0 à 0xCF, sauf DHT (C4), JPG (C8) et DAC (CC). */
const isStartOfFrame = (marker: number) => marker >= 0xc0 && marker <= 0xcf && ![0xc4, 0xc8, 0xcc].includes(marker)

/** Lit dimensions et nombre de composantes dans le premier segment SOF du JPEG. */
export function readJpegInfo(bytes: Uint8Array): JpegInfo {
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8) throw new Error(JPEG_ERROR)
  let offset = 2
  while (offset + 4 <= bytes.length) {
    if (bytes[offset] !== 0xff) throw new Error(JPEG_ERROR)
    const marker = bytes[offset + 1]!
    // Octets de remplissage 0xFF entre deux segments.
    if (marker === 0xff) {
      offset++
      continue
    }
    const length = (bytes[offset + 2]! << 8) | bytes[offset + 3]!
    if (isStartOfFrame(marker)) {
      if (offset + 9 >= bytes.length) break
      const height = (bytes[offset + 5]! << 8) | bytes[offset + 6]!
      const width = (bytes[offset + 7]! << 8) | bytes[offset + 8]!
      const components = bytes[offset + 9]!
      if (!width || !height || ![1, 3, 4].includes(components)) break
      return { width, height, components: components as JpegInfo['components'] }
    }
    // Début des données compressées sans SOF rencontré : fichier invalide.
    if (marker === 0xda || length < 2) break
    offset += 2 + length
  }
  throw new Error(JPEG_ERROR)
}

const COLOR_SPACES = { 1: '/DeviceGray', 3: '/DeviceRGB', 4: '/DeviceCMYK' } as const

/** Nombre PDF : 2 décimales au plus, sans zéros inutiles. */
const num = (value: number) => String(Math.round(value * 100) / 100)

/** Taille de la page (A4 dans l'orientation de l'image) et placement de l'image, centrée sans déformation. */
export function placeOnA4(width: number, height: number) {
  const landscape = width > height
  const page = landscape ? { width: A4.height, height: A4.width } : { ...A4 }
  const scale = Math.min(page.width / width, page.height / height)
  const drawn = { width: width * scale, height: height * scale }
  return { page, x: (page.width - drawn.width) / 2, y: (page.height - drawn.height) / 2, ...drawn }
}

/** Construit un PDF d'une page A4 par image JPEG, dans l'ordre donné. */
export function buildPdf(pages: readonly Uint8Array[]): Uint8Array<ArrayBuffer> {
  if (pages.length === 0) throw new RangeError('Au moins une page est attendue.')

  const encoder = new TextEncoder()
  const chunks: Uint8Array[] = []
  const offsets: number[] = []
  let length = 0

  const write = (chunk: Uint8Array | string) => {
    const bytes = typeof chunk === 'string' ? encoder.encode(chunk) : chunk
    chunks.push(bytes)
    length += bytes.length
  }
  /** Objet n : son décalage est noté pour la table xref. */
  const object = (id: number, body: string, stream?: Uint8Array) => {
    offsets[id] = length
    write(`${id} 0 obj\n${body}\n`)
    if (stream) {
      write('stream\n')
      write(stream)
      write('\nendstream\n')
    }
    write('endobj\n')
  }

  // Numérotation : 1 catalogue, 2 arbre des pages, puis par page : page, contenu, image.
  const pageIds = pages.map((_, index) => 3 + index * 3)

  // En-tête, suivi d'un commentaire binaire : le fichier est traité comme binaire par les outils.
  write('%PDF-1.4\n')
  write(new Uint8Array([0x25, 0xe2, 0xe3, 0xcf, 0xd3, 0x0a]))

  object(1, '<< /Type /Catalog /Pages 2 0 R >>')
  object(2, `<< /Type /Pages /Kids [${pageIds.map((id) => `${id} 0 R`).join(' ')}] /Count ${pages.length} >>`)

  pages.forEach((jpeg, index) => {
    const info = readJpegInfo(jpeg)
    const [pageId, contentId, imageId] = [pageIds[index]!, pageIds[index]! + 1, pageIds[index]! + 2]
    const place = placeOnA4(info.width, info.height)
    const content = encoder.encode(
      `q ${num(place.width)} 0 0 ${num(place.height)} ${num(place.x)} ${num(place.y)} cm /Im0 Do Q`,
    )

    object(
      pageId,
      `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${num(place.page.width)} ${num(place.page.height)}] ` +
        `/Resources << /XObject << /Im0 ${imageId} 0 R >> >> /Contents ${contentId} 0 R >>`,
    )
    object(contentId, `<< /Length ${content.length} >>`, content)
    object(
      imageId,
      `<< /Type /XObject /Subtype /Image /Width ${info.width} /Height ${info.height} ` +
        `/ColorSpace ${COLOR_SPACES[info.components]} /BitsPerComponent 8 /Filter /DCTDecode ` +
        // JPEG CMJN (Adobe) : composantes inversées.
        `${info.components === 4 ? '/Decode [1 0 1 0 1 0 1 0] ' : ''}/Length ${jpeg.length} >>`,
      jpeg,
    )
  })

  // Table des références croisées : une entrée de 20 octets exactement par objet.
  const count = pageIds.length * 3 + 3
  const xref = length
  write(`xref\n0 ${count}\n0000000000 65535 f \n`)
  for (let id = 1; id < count; id++) write(`${String(offsets[id]).padStart(10, '0')} 00000 n \n`)
  write(`trailer\n<< /Size ${count} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`)

  const result = new Uint8Array(length)
  let position = 0
  for (const chunk of chunks) {
    result.set(chunk, position)
    position += chunk.length
  }
  return result
}
