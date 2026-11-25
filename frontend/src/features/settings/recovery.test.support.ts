import { afterEach, beforeEach, vi } from 'vitest'

export function installTestStructureStorage() {
  beforeEach(async () => {
    const held = new Set<string>()
    const { webcrypto } = await vi.importActual<{ webcrypto: Crypto }>('node:crypto')
    vi.stubGlobal('crypto', webcrypto)
    vi.stubGlobal('navigator', Object.assign(Object.create(window.navigator), {
      locks: {
        request: async (name: string, _options: unknown, run: (lock: object | null) => Promise<unknown>) => {
          if (held.has(name)) return run(null)
          held.add(name)
          try { return await run({ name }) } finally { held.delete(name) }
        },
      },
    }))
  })
  afterEach(() => {
    for (const key of Object.keys(localStorage)) if (key.startsWith('medipos-structure-v1/')) localStorage.removeItem(key)
    for (const key of Object.keys(sessionStorage)) if (key.startsWith('medipos-operation-addresses-v1/')) sessionStorage.removeItem(key)
    vi.unstubAllGlobals()
  })
}
