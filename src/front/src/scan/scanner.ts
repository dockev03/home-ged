import { MAX_PAGE_SIDE, type Quad } from './geometry'
import type { ScanFilter, ScanRequest, ScanResponse } from './protocol'

/** Erreur du module de recadrage, avec un message affichable. */
export class ScanError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'ScanError'
  }
}

/** Ce que l'écran Scanner attend du worker : détecter les coins puis redresser la page. */
export interface Scanner {
  /** Confie la photo (transférée : `image` devient inutilisable) ; renvoie les coins détectés ou null. */
  charger(image: ImageData): Promise<Quad | null>
  /** Page redressée et filtrée, à partir de la dernière photo confiée. */
  redresser(coins: Quad, filtre: ScanFilter, coteMax?: number): Promise<ImageData>
  /** Arrête le worker et libère la mémoire (photo, OpenCV). */
  fermer(): void
}

/** Sous-ensemble de Worker utilisé, pour substituer un faux worker dans les tests. */
export interface ScanWorker {
  postMessage(message: ScanRequest, transfer: Transferable[]): void
  terminate(): void
  onmessage: ((event: MessageEvent<ScanResponse>) => void) | null
  onerror: ((event: ErrorEvent) => void) | null
}

/** Requête sans identifiant (Omit distribué sur chaque variante). */
type WithoutId<T> = T extends unknown ? Omit<T, 'id'> : never
type Request = WithoutId<ScanRequest>

export const LOAD_ERROR = 'Le module de recadrage n’a pas pu démarrer. Rechargez la page et réessayez.'

const createWorker = (): ScanWorker => new Worker(new URL('./opencv.worker.ts', import.meta.url), { type: 'module', name: 'recadrage' })

/** Client du worker de recadrage, créé à la demande : OpenCV (environ 10 Mo) ne se charge qu'à la première photo. */
export function createScanner(factory: () => ScanWorker = createWorker): Scanner {
  let worker: ScanWorker | null = null
  let nextId = 1
  const pending = new Map<number, { resolve: (r: ScanResponse) => void; reject: (e: Error) => void }>()

  function failAll(message: string) {
    for (const { reject } of pending.values()) reject(new ScanError(message))
    pending.clear()
  }

  function start(): ScanWorker {
    if (worker) return worker
    const created = factory()
    created.onmessage = (event) => {
      const entry = pending.get(event.data.id)
      pending.delete(event.data.id)
      entry?.resolve(event.data)
    }
    // Script introuvable, CSP refusée, mémoire épuisée… : le worker est perdu, on repartira d'un neuf.
    created.onerror = (event) => {
      event.preventDefault()
      created.terminate()
      worker = null
      failAll(LOAD_ERROR)
    }
    worker = created
    return created
  }

  function send(request: Request, transfer: Transferable[] = []): Promise<ScanResponse> {
    const target = start()
    const id = nextId++
    return new Promise<ScanResponse>((resolve, reject) => {
      pending.set(id, { resolve, reject })
      target.postMessage({ ...request, id } as ScanRequest, transfer)
    }).then((response) => {
      if ('erreur' in response) throw new ScanError(response.erreur)
      return response
    })
  }

  return {
    async charger(image) {
      const response = await send({ type: 'charger', image }, [image.data.buffer])
      return 'coins' in response ? response.coins : null
    },
    async redresser(coins, filtre, coteMax = MAX_PAGE_SIDE) {
      // Copie simple : des coins réactifs (Proxy de Vue) ne passent pas le clonage de postMessage.
      const plain = coins.map(({ x, y }) => ({ x, y })) as Quad
      const response = await send({ type: 'redresser', coins: plain, filtre, coteMax })
      if (!('image' in response)) throw new ScanError('Réponse inattendue du module de recadrage.')
      return response.image
    },
    fermer() {
      worker?.terminate()
      worker = null
      failAll('Recadrage interrompu.')
    },
  }
}
