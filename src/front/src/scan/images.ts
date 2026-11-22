import { fitWithin, MAX_SOURCE_SIDE } from './geometry'

/** Photo décodée : pixels pour le worker et aperçu affichable, aux mêmes dimensions. */
export interface DecodedPhoto {
  image: ImageData
  /** URL `blob:` de l'aperçu, à libérer avec URL.revokeObjectURL. */
  preview: string
  width: number
  height: number
}

export const DECODE_ERROR = 'Cette image ne peut pas être lue. Essayez une photo au format JPEG ou PNG.'

function canvas(width: number, height: number) {
  const element = document.createElement('canvas')
  element.width = width
  element.height = height
  const context = element.getContext('2d')
  if (!context) throw new Error('Canvas indisponible.')
  return { element, context }
}

function toBlob(element: HTMLCanvasElement, type: string, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) =>
    element.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Image impossible à encoder.'))), type, quality),
  )
}

/**
 * Décode une photo (orientation EXIF appliquée), réduite à MAX_SOURCE_SIDE : une photo de 48 Mpx
 * ferait près de 200 Mo de pixels, de quoi épuiser la mémoire d'un téléphone.
 */
export async function decodePhoto(file: Blob): Promise<DecodedPhoto> {
  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error(DECODE_ERROR)
  }
  try {
    const { width, height } = fitWithin(bitmap.width, bitmap.height, MAX_SOURCE_SIDE)
    const { element, context } = canvas(width, height)
    context.drawImage(bitmap, 0, 0, width, height)
    const image = context.getImageData(0, 0, width, height)
    const preview = URL.createObjectURL(await toBlob(element, 'image/jpeg', 0.8))
    return { image, preview, width, height }
  } finally {
    bitmap.close()
  }
}

/** Encode une page redressée en JPEG (qualité suffisante pour l'OCR, fichier léger). */
export async function encodePage(page: ImageData, quality = 0.85): Promise<Blob> {
  const { element, context } = canvas(page.width, page.height)
  context.putImageData(page, 0, 0)
  return toBlob(element, 'image/jpeg', quality)
}
