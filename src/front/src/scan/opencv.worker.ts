/// <reference lib="webworker" />
/**
 * Worker de recadrage (SCAN-02, 03) : détection des bords, perspective et filtres avec OpenCV.js.
 *
 * OpenCV.js exige `'unsafe-eval'` : le BFF sert ce script avec une CSP qui ne l'autorise qu'ici
 * (SecurityHeaders.ScanWorkerContentSecurityPolicy). Worker module : OpenCV est importé
 * dynamiquement, à la première demande.
 */
import { fitWithin, insetQuad, isConvexQuad, orderCorners, pageSize, quadArea, scaleQuad, type Point, type Quad } from './geometry'
import type { OpenCv } from './opencv'
import type { ScanFilter, ScanRequest, ScanResponse } from './protocol'

type Mat = InstanceType<OpenCv['Mat']>

declare const self: DedicatedWorkerGlobalScope

/** Côté de l'image analysée pour la détection : assez pour trouver les bords, rapide sur mobile. */
const DETECT_SIDE = 1000
/** Une page détectée couvre au moins 20 % de la photo ; en dessous, c'est un autre objet. */
const MIN_PAGE_RATIO = 0.2
/** Retrait des coins détectés, en pixels de l'image analysée : épaisseur du bord dilaté. */
const DETECT_INSET = 2

/** OpenCV emballé dans un objet : le module, « thenable », ne doit jamais être une valeur de promesse. */
async function loadOpenCv(): Promise<{ cv: OpenCv }> {
  const { waitForOpenCv, getOpenCv } = await import('./opencv')
  await waitForOpenCv()
  return { cv: getOpenCv() }
}

/** Photo confiée par la page, gardée pour les redressements successifs (changement de filtre, de coins). */
let source: Mat | null = null

function detect(cv: OpenCv, image: Mat): Quad | null {
  const { width, height, scale } = fitWithin(image.cols, image.rows, DETECT_SIDE)
  const mats: Mat[] = []
  const track = <T extends Mat>(mat: T) => (mats.push(mat), mat)
  const contours = new cv.MatVector()
  try {
    const small = track(new cv.Mat())
    cv.resize(image, small, new cv.Size(width, height), 0, 0, cv.INTER_AREA)
    const gray = track(new cv.Mat())
    cv.cvtColor(small, gray, cv.COLOR_RGBA2GRAY)
    cv.GaussianBlur(gray, gray, new cv.Size(5, 5), 0)
    const edges = track(new cv.Mat())
    cv.Canny(gray, edges, 50, 150)
    const kernel = track(cv.Mat.ones(3, 3, cv.CV_8U))
    cv.dilate(edges, edges, kernel)
    const hierarchy = track(new cv.Mat())
    cv.findContours(edges, contours, hierarchy, cv.RETR_LIST, cv.CHAIN_APPROX_SIMPLE)

    let best: Quad | null = null
    let bestArea = MIN_PAGE_RATIO * width * height
    for (let i = 0; i < contours.size(); i++) {
      const contour = contours.get(i)
      const approx = new cv.Mat()
      try {
        cv.approxPolyDP(contour, approx, 0.02 * cv.arcLength(contour, true), true)
        if (approx.rows !== 4) continue
        const points: Point[] = []
        for (let k = 0; k < 4; k++) points.push({ x: approx.data32S[k * 2]!, y: approx.data32S[k * 2 + 1]! })
        const quad = orderCorners(points)
        const area = quadArea(quad)
        if (area > bestArea && isConvexQuad(quad)) {
          best = quad
          bestArea = area
        }
      } finally {
        approx.delete()
        contour.delete()
      }
    }
    return best && scaleQuad(insetQuad(best, DETECT_INSET), 1 / scale)
  } finally {
    contours.delete()
    mats.forEach((m) => m.delete())
  }
}

function applyFilter(cv: OpenCv, page: Mat, filtre: ScanFilter): Mat {
  if (filtre === 'couleur') return page
  const gray = new cv.Mat()
  cv.cvtColor(page, gray, cv.COLOR_RGBA2GRAY)
  if (filtre === 'scan') {
    // Fond estimé (le texte effacé par dilatation puis flou médian) et retiré : papier blanc,
    // ombres et éclairage inégal gommés, encre renforcée.
    const background = new cv.Mat()
    const kernel = cv.Mat.ones(7, 7, cv.CV_8U)
    try {
      cv.dilate(gray, background, kernel)
      cv.medianBlur(background, background, 21)
      cv.absdiff(gray, background, gray)
      cv.bitwise_not(gray, gray)
      cv.normalize(gray, gray, 0, 255, cv.NORM_MINMAX)
    } finally {
      background.delete()
      kernel.delete()
    }
  }
  const rgba = new cv.Mat()
  cv.cvtColor(gray, rgba, cv.COLOR_GRAY2RGBA)
  gray.delete()
  return rgba
}

function warp(cv: OpenCv, image: Mat, coins: Quad, filtre: ScanFilter, coteMax: number): ImageData {
  const { width, height } = pageSize(coins, coteMax)
  const from = cv.matFromArray(4, 1, cv.CV_32FC2, coins.flatMap((p) => [p.x, p.y]))
  const to = cv.matFromArray(4, 1, cv.CV_32FC2, [0, 0, width, 0, width, height, 0, height])
  const transform = cv.getPerspectiveTransform(from, to)
  const page = new cv.Mat()
  let filtered: Mat | null = null
  try {
    cv.warpPerspective(image, page, transform, new cv.Size(width, height), cv.INTER_LINEAR, cv.BORDER_REPLICATE)
    filtered = applyFilter(cv, page, filtre)
    return new ImageData(new Uint8ClampedArray(filtered.data), width, height)
  } finally {
    if (filtered && filtered !== page) filtered.delete()
    for (const mat of [from, to, transform, page]) mat.delete()
  }
}

async function handle(request: ScanRequest): Promise<ScanResponse> {
  const { cv } = await loadOpenCv()
  if (request.type === 'charger') {
    source?.delete()
    source = cv.matFromImageData(request.image)
    return { id: request.id, coins: detect(cv, source) }
  }
  if (!source) return { id: request.id, erreur: 'Aucune photo à redresser.' }
  const image = warp(cv, source, request.coins, request.filtre, request.coteMax)
  return { id: request.id, image }
}

self.onmessage = async (event: MessageEvent<ScanRequest>) => {
  let response: ScanResponse
  try {
    response = await handle(event.data)
  } catch (e) {
    // Exception OpenCV : un pointeur numérique, sans message exploitable.
    response = { id: event.data.id, erreur: e instanceof Error ? e.message : 'Erreur de traitement de l’image.' }
  }
  self.postMessage(response, 'image' in response ? [response.image.data.buffer] : [])
}
