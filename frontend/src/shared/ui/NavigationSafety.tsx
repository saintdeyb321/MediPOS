import { useEffect, useReducer, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useBeforeUnload, useBlocker, useLocation, useNavigationType } from 'react-router'
import { Modal } from './Modal'
import { Button } from './Button'
import { NavigationRegistry } from './NavigationProtection'

const hasPending = (search: string) => [...new URLSearchParams(search).keys()].some(key => /pending|closingCash|notificationRead|^invitation(Request|Target)$/i.test(key))
export function NavigationSafety({ identity, children }: { identity?: string; children: ReactNode }) {
  const [, render] = useReducer((n: number) => n + 1, 0)
  const [registry] = useState(() => ({ entries: new Map<string, { dirty?: boolean; busy?: boolean; pending?: boolean }>(), notify: () => render() }))
  const [storageError, setStorageError] = useState(false)
  const location = useLocation(); const navigationType = useNavigationType()
  const storageKey = identity ? 'medipos-operation-addresses-v1/' + identity : null
  function read(): string[] { try { const stored: unknown = JSON.parse(storageKey ? sessionStorage.getItem(storageKey) ?? '[]' : '[]'); return Array.isArray(stored) ? stored.filter((v): v is string => typeof v === 'string' && (/^\/boticas\/[0-9a-f-]+\/sucursales\/[0-9a-f-]+\//i.test(v) || /^\/plataforma\?/.test(v)) && !v.includes('#')) : [] } catch { return [] } }
  function remember() {
    if (!storageKey || !hasPending(location.search)) return false
    try { const address = location.pathname + location.search; sessionStorage.setItem(storageKey, JSON.stringify([...read().filter(v => v.split('?')[0] !== location.pathname), address])); return true } catch { return false }
  }
  useEffect(() => {
    if (!storageKey) return
    if (hasPending(location.search)) remember()
    else if (navigationType === 'REPLACE') { try { sessionStorage.setItem(storageKey, JSON.stringify(read().filter(v => v.split('?')[0] !== location.pathname))) } catch { /* A pending operation is never cleared from a failed storage write. */ } }
  // The record is only an identity-scoped recovery address, never request bodies, cookies or tokens.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [storageKey, location.pathname, location.search, navigationType])
  const active = () => [...registry.entries.values()].some(v => v.busy) || Boolean(document.querySelector('dialog[open] .button[aria-busy="true"]'))
  const modified = () => [...registry.entries.values()].some(v => v.dirty || v.pending) || hasPending(location.search)
  const blocker = useBlocker(({ currentLocation, nextLocation, historyAction }) => Boolean(identity) && (currentLocation.pathname !== nextLocation.pathname || historyAction !== 'REPLACE' && hasPending(currentLocation.search) && currentLocation.search !== nextLocation.search) && (active() || modified()))
  useBeforeUnload(event => { if (active() || modified()) { remember(); event.preventDefault(); event.returnValue = '' } })
  const pending = hasPending(location.search)
  const recovery = read().filter(v => v !== location.pathname + location.search)
  return <NavigationRegistry.Provider value={registry}>
    {recovery.length > 0 && <aside className="notice warning" aria-label="Operaciones pendientes de esta cuenta"><strong>Operaciones pendientes de verificar</strong><ul>{recovery.map(address => <li key={address}><Link to={address}>Volver a la operación original sin reenviar</Link></li>)}</ul></aside>}
    {children}
    <Modal trackNavigation={false} open={blocker.state === 'blocked'} title="Antes de salir" onClose={() => { if (blocker.state === 'blocked') blocker.reset() }} busy={active()}>
      <p>{active() ? 'Hay una petición activa. Espera a que termine; salir no demuestra que el servidor la canceló.' : pending ? 'La operación original sigue por conciliar. Puedes volver a ella desde el aviso de recuperación de esta cuenta.' : 'Los cambios locales aún no se guardaron. Salir los descartará; los registros persistidos se conservan.'}</p>
      <Button variant="secondary" disabled={active()} onClick={() => { if (blocker.state === 'blocked') blocker.reset() }}>Permanecer aquí</Button>
      {storageError && <p role="alert">No se pudo conservar la dirección de recuperación en este navegador. Permanece aquí y concilia la operación original antes de salir.</p>}
      <Button disabled={active()} onClick={() => { setStorageError(false); if (blocker.state !== 'blocked') return; if (pending && !remember()) { setStorageError(true); return }; blocker.proceed() }}>{pending ? 'Salir conservando recuperación' : 'Descartar cambios y salir'}</Button>
    </Modal>
  </NavigationRegistry.Provider>
}
