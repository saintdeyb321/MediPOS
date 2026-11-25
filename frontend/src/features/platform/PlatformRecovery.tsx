import { useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { DateText } from '../../shared/ui/DateText'
import { licenseLabel } from '../../shared/ui/licenseLabel'
import { ownerLabel } from './presentation'
import type { PlatformConsoleState } from './PlatformTenantDirectory'

const pendingActions = new Map([
  ['renew', 'Renovar licencia'], ['suspend', 'Suspender licencia'], ['reactivate', 'Reactivar licencia'],
  ['owner-change', 'Corregir Gmail pendiente'], ['owner-revoke', 'Revocar asignación pendiente'],
])

export function PlatformRecovery({ p, kind, recovered }: { p: PlatformConsoleState; kind: 'creation' | 'operation' | 'category'; recovered?: () => void }) {
  const [confirmed, setConfirmed] = useState(false)
  if (kind === 'creation') return p.creationKey && <Notice tone="warning">Alta pendiente de conciliación. No crees otra botica ni cambies la clave. Una lectura vacía no demuestra rollback.
    <Button disabled={p.busy || p.cooldown > 0 || p.denied || !p.admitted} onClick={() => void p.reconcileCreation().then(saved => { if (saved) recovered?.() })}>Consultar constancia original sin repetir POST</Button>
  </Notice>
  if (kind === 'operation') return p.pending && !p.creationKey && <Notice tone="warning">{pendingActions.get(p.pending) ?? 'Operación administrativa'} pendiente de conciliación.
    <Button disabled={p.busy || p.cooldown > 0 || p.denied || !p.admitted} onClick={() => { setConfirmed(false); void p.refresh() }}>{p.pendingLicense ? 'Reconsultar licencia' : 'Reconsultar licencia y propietario'}</Button>
    {p.reconciliationRead && p.data && <div className="platform-recovery-result">
      <p><strong>{p.data.tenant.tradingName}</strong></p>
      <p>Licencia consultada: {licenseLabel(p.data.license.status)} · vence <DateText value={p.data.license.expiresAt} />.</p>
      {!p.pendingLicense && <p>{p.initialOwner.data ? <>{ownerLabel(p.initialOwner.data.status)} · {p.initialOwner.data.email}</> : 'Sin designación por Gmail.'}</p>}
    </div>}
    <label><input type="checkbox" disabled={p.busy || !p.reconciliationRead} checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> Verifiqué el resultado original y que no hay solicitud en curso.</label>
    <Button disabled={p.busy || p.cooldown > 0 || !p.admitted || !confirmed || !p.reconciliationRead} onClick={() => { p.acknowledge(); setConfirmed(false); recovered?.() }}>Continuar tras conciliación</Button>
  </Notice>
  const matches = p.categoriesRead ? p.categories.data?.filter(row => row.name.trim().toLocaleLowerCase('es-PE') === p.categoryPending?.trim().toLocaleLowerCase('es-PE')) ?? [] : []
  return p.categoryPending && <Notice tone="warning">Alta de categoría pendiente: {p.categoryPending}. Esta operación no tiene constancia idempotente: verifica el resultado sin reenviarla.
    <Button disabled={p.busy || p.cooldown > 0 || !p.admitted} onClick={() => { setConfirmed(false); void p.readCategories() }}>Reconsultar categorías</Button>
    {matches.length === 1 && <p>Categoría observada: {matches[0]?.name} · <DateText value={matches[0]!.createdAt} />.</p>}
    {matches.length > 1 && <p>Hay categorías con el mismo nombre. La conciliación permanece bloqueada.</p>}
    <label><input type="checkbox" disabled={p.busy || matches.length !== 1} checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> Verifiqué la categoría registrada y que la petición original terminó.</label>
    <Button disabled={p.busy || p.cooldown > 0 || !p.admitted || !confirmed || matches.length !== 1} onClick={() => { p.acknowledgeCategory(); setConfirmed(false); recovered?.() }}>Continuar tras conciliar categoría</Button>
  </Notice>
}
