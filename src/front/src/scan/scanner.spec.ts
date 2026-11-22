import { describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { fullFrame } from './geometry'
import type { ScanRequest, ScanResponse } from './protocol'
import { createScanner, LOAD_ERROR, ScanError, type ScanWorker } from './scanner'

/** Faux worker : garde les messages reçus, répond à la demande. */
class FakeWorker implements ScanWorker {
  onmessage: ScanWorker['onmessage'] = null
  onerror: ScanWorker['onerror'] = null
  received: { message: ScanRequest; transfer: Transferable[] }[] = []
  terminate = vi.fn()

  postMessage(message: ScanRequest, transfer: Transferable[]) {
    this.received.push({ message, transfer })
  }

  reply(response: ScanResponse) {
    this.onmessage?.(new MessageEvent('message', { data: response }))
  }

  crash() {
    this.onerror?.(new ErrorEvent('error', { cancelable: true }))
  }
}

/** ImageData n'existe pas dans jsdom : un objet de même forme suffit. */
const imageData = (width = 2, height = 1) => ({ width, height, data: new Uint8ClampedArray(width * height * 4) }) as ImageData

function setup() {
  const workers: FakeWorker[] = []
  const factory = vi.fn(() => {
    const worker = new FakeWorker()
    workers.push(worker)
    return worker
  })
  return { scanner: createScanner(factory), factory, workers }
}

describe('client du worker de recadrage', () => {
  it('ne démarre le worker qu’à la première photo', async () => {
    const { scanner, factory, workers } = setup()
    expect(factory).not.toHaveBeenCalled()

    const image = imageData()
    const result = scanner.charger(image)
    const [{ message, transfer }] = workers[0]!.received
    expect(message).toEqual({ id: 1, type: 'charger', image })
    // La photo est transférée, pas copiée.
    expect(transfer).toEqual([image.data.buffer])

    workers[0]!.reply({ id: 1, coins: fullFrame(2, 1) })
    await expect(result).resolves.toEqual(fullFrame(2, 1))
    expect(factory).toHaveBeenCalledTimes(1)
  })

  it('renvoie null quand aucune page n’est détectée', async () => {
    const { scanner, workers } = setup()
    const result = scanner.charger(imageData())
    workers[0]!.reply({ id: 1, coins: null })
    await expect(result).resolves.toBeNull()
  })

  it('associe chaque réponse à sa demande', async () => {
    const { scanner, workers } = setup()
    const first = scanner.redresser(fullFrame(2, 1), 'scan')
    const second = scanner.redresser(fullFrame(2, 1), 'gris', 800)
    expect(workers[0]!.received.map((r) => r.message)).toEqual([
      { id: 1, type: 'redresser', coins: fullFrame(2, 1), filtre: 'scan', coteMax: 2500 },
      { id: 2, type: 'redresser', coins: fullFrame(2, 1), filtre: 'gris', coteMax: 800 },
    ])

    const page = imageData(4, 4)
    workers[0]!.reply({ id: 2, image: page })
    workers[0]!.reply({ id: 1, erreur: 'Aucune photo à redresser.' })
    await expect(second).resolves.toBe(page)
    await expect(first).rejects.toEqual(new ScanError('Aucune photo à redresser.'))
  })

  it('envoie des coins clonables, même réactifs', () => {
    const { scanner, workers } = setup()
    void scanner.redresser(reactive(fullFrame(2, 1)) as ReturnType<typeof fullFrame>, 'scan')

    const { message } = workers[0]!.received[0]!
    expect(() => structuredClone(message)).not.toThrow()
  })

  it('signale un worker perdu et en démarre un neuf ensuite', async () => {
    const { scanner, factory, workers } = setup()
    const result = scanner.charger(imageData())
    workers[0]!.crash()

    await expect(result).rejects.toEqual(new ScanError(LOAD_ERROR))
    expect(workers[0]!.terminate).toHaveBeenCalled()

    void scanner.charger(imageData()).catch(() => {})
    expect(factory).toHaveBeenCalledTimes(2)
  })

  it('arrête le worker en abandonnant les demandes en cours', async () => {
    const { scanner, workers } = setup()
    const result = scanner.charger(imageData())
    scanner.fermer()

    await expect(result).rejects.toBeInstanceOf(ScanError)
    expect(workers[0]!.terminate).toHaveBeenCalled()
  })
})
