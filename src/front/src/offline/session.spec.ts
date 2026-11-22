import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { memoryBackend, offlineDb, setOfflineBackend, type StoreName } from './db'
import { expireSession, forgetEverything, lastUser, rememberUser } from './session'

const ALICE = { id: 1, username: 'alice' }
const BOB = { id: 2, username: 'bob' }

async function fill() {
  await offlineDb.put('documents', { key: '4' })
  await offlineDb.put('fichiers', { key: '4:apercu' })
  await offlineDb.put('envois', { key: 'e1' })
}

const keys = async (store: StoreName) => (await offlineDb.getAll(store)).map((r) => r.key)

describe('session hors ligne', () => {
  beforeEach(() => setOfflineBackend(memoryBackend()))
  afterEach(() => setOfflineBackend(null))

  it('retient le dernier utilisateur pour démarrer hors ligne', async () => {
    expect(await lastUser()).toBeNull()
    await rememberUser(ALICE)
    expect(await lastUser()).toEqual(ALICE)
  })

  it('le même utilisateur garde ses données, un autre compte efface tout', async () => {
    await rememberUser(ALICE)
    await fill()

    await rememberUser(ALICE)
    expect(await keys('envois')).toEqual(['e1'])

    await rememberUser(BOB)
    expect(await keys('documents')).toEqual([])
    expect(await keys('fichiers')).toEqual([])
    expect(await keys('envois')).toEqual([])
    expect(await lastUser()).toEqual(BOB)
  })

  it('à la déconnexion, rien ne reste sur l’appareil', async () => {
    await rememberUser(ALICE)
    await fill()

    await forgetEverything()

    for (const store of ['documents', 'fichiers', 'envois', 'session'] as const) expect(await keys(store)).toEqual([])
  })

  it('session expirée : documents effacés, envois gardés, plus de démarrage hors ligne', async () => {
    await rememberUser(ALICE)
    await fill()

    await expireSession()

    expect(await keys('documents')).toEqual([])
    expect(await keys('fichiers')).toEqual([])
    expect(await keys('envois')).toEqual(['e1'])
    expect(await lastUser()).toBeNull()

    // Un autre compte qui se connecte ensuite efface les envois restés en attente.
    await rememberUser(BOB)
    expect(await keys('envois')).toEqual([])
  })
})
