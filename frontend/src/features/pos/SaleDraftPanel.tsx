import { useState } from 'react'
import { ApiErrorState, isUuid } from '../access'
import { Panel } from '../../shared/ui/Panel'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { decimalLabel } from '../../shared/api/decimal'
import type { DraftController } from './useSaleDraft'

export function SaleDraftPanel({ draft }: { draft: DraftController }) {
  const [recoverId, setRecoverId] = useState('')
  const busy = draft.phase === 'creating' || draft.phase === 'saving' || draft.phase === 'loading'
  return <Panel className="draft-panel" aria-labelledby="draft-title">
    <div className="panel-heading"><h2 id="draft-title">Borrador en el servidor</h2>
      <Badge tone={draft.server && !draft.dirty ? 'success' : 'warning'}>{draft.server ? draft.dirty ? 'Cambios locales pendientes' : 'Líneas persistidas' : 'Sin borrador identificado'}</Badge></div>
    <div className="pos-section-body">
      {draft.error != null && <ApiErrorState error={draft.error} title="Revisa el borrador antes de continuar" />}
      {draft.notice && <p role="status">{draft.notice}</p>}
      {draft.creationUnknown && <Notice tone="warning">La creación tiene un resultado incierto y no recibimos su identificador. No crees otra venta. Un operador debe reconciliar el registro antes de recuperar su identificador aquí.</Notice>}
      {!draft.cashMatches && <p>Necesitas tu caja abierta en esta sucursal; si ya existe un borrador, debe corresponder a esa misma caja.</p>}
      {!draft.seller && <Notice tone="warning">Este borrador pertenece a otra membresía. La lectura no concede permiso de edición.</Notice>}
      {draft.server && <section aria-label="Estado persistido del borrador" className="saved-draft">
        <p>Estado del servidor: <strong>{draft.server.status === 'draft' ? 'Borrador, sin confirmar' : draft.server.status === 'confirmed' ? 'Confirmada, edición bloqueada' : 'Anulada, edición bloqueada'}</strong> · Versión {draft.server.version}</p>
        <ul>{draft.server.lines.map(line => <li key={line.saleLineId}>{line.productNameSnapshot} · {line.unitNameSnapshot} × {decimalLabel(line.quantity)} — S/ {decimalLabel(line.lineTotal, 2)}</li>)}</ul>
        <p><strong>Total guardado por el servidor: S/ {decimalLabel(draft.server.totalAmount, 2)}</strong></p>
        <p className="muted">La dirección de esta página conserva el identificador para recuperar este registro al recargar. El carrito no se guarda en el dispositivo.</p>
      </section>}
      <div className="pos-actions">
        {!draft.server ? <Button onClick={() => void draft.create()} pending={draft.phase === 'creating'} disabled={!draft.canCreate}>Crear borrador</Button> :
          <Button onClick={() => void draft.save()} pending={draft.phase === 'saving'} disabled={!draft.canSave}>Guardar líneas</Button>}
        {(draft.server || draft.hasKnownId) && <Button variant="secondary" disabled={busy} onClick={() => void draft.refresh()}>Consultar borrador guardado</Button>}
      </div>
      {draft.phase === 'conflict' && <Notice tone="warning"><p>La versión o las líneas difieren. Compara el carrito con el estado persistido antes de elegir. Ninguna opción envía una escritura.</p>
        <div className="pos-actions"><Button variant="secondary" onClick={() => draft.resolve(true)}>Reemplazar carrito por servidor</Button>
          <Button variant="secondary" onClick={() => draft.resolve(false)}>Revisado: conservar mi carrito</Button></div></Notice>}
      {(draft.phase === 'uncertain' || draft.phase === 'blocked') && !draft.creationUnknown && !draft.server && <p>Recupera el identificador conocido abajo. No se generará otro borrador automáticamente.</p>}
      <details><summary>Recuperar un borrador por su identificador</summary>
        <form onSubmit={event => { event.preventDefault(); if (isUuid(recoverId.trim())) draft.recover(recoverId.trim()) }}>
          <TextField label="Identificador del borrador" value={recoverId} maxLength={36} autoComplete="off" spellCheck={false}
            onChange={event => setRecoverId(event.target.value)} hint="Debe pertenecer a la botica y sucursal actuales. Recuperarlo reemplaza el carrito local por las líneas del servidor." />
          <Button type="submit" variant="secondary" disabled={busy || draft.checkoutPending || !isUuid(recoverId.trim())}>Recuperar y reemplazar carrito</Button>
        </form>
      </details>
    </div>
  </Panel>
}
