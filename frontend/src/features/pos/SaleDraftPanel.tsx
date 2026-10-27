import { useState } from 'react'
import { ApiErrorState, isUuid } from '../access'
import { Panel } from '../../shared/ui/Panel'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { decimalLabel } from '../../shared/api/decimal'
import type { DraftController } from './useSaleDraft'

// Exceptional recovery only. Normal selling never exposes draft administration.
export function SaleDraftPanel({ draft }: { draft: DraftController }) {
  const [recoverId, setRecoverId] = useState('')
  const exceptional = draft.creationUnknown || draft.phase === 'uncertain' || draft.phase === 'conflict' || draft.phase === 'blocked' || draft.error != null
  if (!exceptional) return null
  const busy = draft.preparing || draft.phase === 'loading' || draft.phase === 'creating' || draft.phase === 'saving'
  return <Panel className="draft-panel" aria-labelledby="recovery-title">
    <div className="panel-heading"><h2 id="recovery-title">Recuperar venta pendiente</h2></div>
    <div className="pos-section-body">
      {draft.error != null && <ApiErrorState error={draft.error} title="No se pudo preparar la venta" />}
      {draft.creationUnknown && <Button disabled={busy} onClick={() => void draft.recoverCreation()}>Consultar constancia del borrador original</Button>}
      {draft.creationUnknown && <Notice tone="warning">No recibimos el identificador de la venta. No crees otra. Un operador debe localizar la operación antes de recuperarla.</Notice>}
      {draft.notice && <p role="status">{draft.notice}</p>}
      {draft.server && <p>Total consultado en el servidor: S/ {decimalLabel(draft.server.totalAmount, 2)}</p>}
      {draft.hasKnownId && <Button variant="secondary" disabled={busy} onClick={() => void draft.refresh()}>Consultar venta pendiente</Button>}
      {draft.phase === 'conflict' && !draft.checkoutPending && <Notice tone="warning"><p>La venta guardada difiere de tu carrito. Revisa antes de continuar. Estas opciones no envían escrituras.</p>
        <div className="pos-actions"><Button variant="secondary" onClick={() => draft.resolve(true)}>Usar venta guardada</Button>
          <Button variant="secondary" onClick={() => draft.resolve(false)}>Conservar mi carrito revisado</Button></div></Notice>}
      {!draft.checkoutPending && <details><summary>Recuperación excepcional por identificador</summary>
        <form onSubmit={event => { event.preventDefault(); if (isUuid(recoverId.trim())) void draft.recover(recoverId.trim()) }}>
          <TextField label="Identificador de la venta pendiente" value={recoverId} maxLength={36} autoComplete="off" spellCheck={false}
            onChange={event => setRecoverId(event.target.value)} hint="Solo ante un resultado incierto. El operador debe verificar la operación; se consultará en esta botica y sucursal." />
          <Button type="submit" variant="secondary" disabled={busy || !isUuid(recoverId.trim())}>Recuperar y reemplazar carrito</Button>
        </form>
      </details>}
    </div>
  </Panel>
}
