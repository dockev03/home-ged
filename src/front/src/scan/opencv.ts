/**
 * Chargement d'OpenCV.js, isolé dans son propre module : le worker l'importe dynamiquement, si bien
 * qu'OpenCV (environ 10 Mo) forme un fichier à part, téléchargé à la première photo seulement.
 *
 * Le module emscripten est « thenable » (Module.then rappelle sa fonction avec le module lui-même) :
 * résoudre une promesse avec lui, ou l'exporter tel quel d'un module importé dynamiquement, bouclerait
 * sans fin. On l'expose donc par une fonction, jamais comme valeur de promesse.
 */
import * as namespace from '@techstark/opencv-js'

export type OpenCv = typeof namespace

/** Le paquet est en CommonJS : l'objet Module réel est l'export par défaut créé par l'interopérabilité. */
const module = ((namespace as unknown as { default?: OpenCv }).default ?? namespace) as OpenCv & {
  then?: (callback: () => void) => void
}

let ready: Promise<void> | null = null

/** Attend la fin de l'initialisation (compilation du WebAssembly). */
export function waitForOpenCv(): Promise<void> {
  ready ??= new Promise<void>((resolve) => {
    if (module.Mat) resolve()
    else if (module.then) module.then(() => resolve())
    else (module as unknown as { onRuntimeInitialized: () => void }).onRuntimeInitialized = () => resolve()
  })
  return ready
}

/** Module OpenCV, à n'utiliser qu'après waitForOpenCv(). */
export const getOpenCv = (): OpenCv => module
