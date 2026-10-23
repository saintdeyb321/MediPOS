import { useState } from 'react'
import { ApiErrorState } from '../access'
import { Panel } from '../../shared/ui/Panel'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { Modal } from '../../shared/ui/Modal'
import { FormFooter } from '../../shared/ui/FormLayout'
import { decimalInput, decimalLabel, maximumMoney } from '../../shared/api/decimal'
import type { CashController } from './useCashSession'

export function CashSessionPanel({ cash }: { cash: CashController }) {
  const [amount, setAmount] = useState('')
  const [opening, setOpening] = useState(false)
  const valid = decimalInput(amount, maximumMoney, 4, true)
  return <Panel className="pos-cash" aria-labelledby="cash-title">
    <div className="panel-heading"><h2 id="cash-title">Tu caja en esta sucursal</h2><Badge tone={cash.session ? 'success' : 'neutral'}>
      {cash.query.isPending ? 'Consultando caja' : cash.query.isError ? 'Estado sin verificar' : cash.session ? 'Caja abierta' : 'Sin caja abierta'}</Badge></div>
    <div className="pos-section-body">
      {!cash.connected && <p>La caja requiere una conexión real con MediPOS.</p>}
      {cash.query.isError && <ApiErrorState error={cash.query.error} title="No pudimos consultar la caja" />}
      {cash.error != null && <ApiErrorState error={cash.error} title="Revisa el resultado de apertura" />}
      {cash.session && cash.uncertain && <Notice tone="warning">Se encontró una caja abierta. Pulsa Consultar caja para reconciliar la apertura pendiente antes de crear un borrador.</Notice>}
      {cash.session ? <p>Monto inicial registrado: S/ {decimalLabel(cash.session.openingAmount, 2)}. Tu caja está abierta en esta sucursal.</p> :
        cash.uncertain ? <Notice tone="warning">La apertura tiene un resultado pendiente. Consulta la caja; no se enviará otra apertura automáticamente.</Notice> :
          !cash.query.isPending && !cash.query.isError && cash.connected && <Button disabled={cash.pending} onClick={() => setOpening(true)}>Abrir mi caja</Button>}
      <Button variant="quiet" onClick={() => void cash.refresh()} disabled={cash.pending || cash.query.isFetching || !cash.connected}>Consultar caja</Button>
      {cash.uncertain && !opening && <Button variant="secondary" disabled={cash.pending} onClick={() => setOpening(true)}>Revisar apertura pendiente sin reenviar</Button>}
    </div>
    <Modal open={opening && (!cash.session || cash.uncertain)} title="Abrir caja en esta sucursal" onClose={() => { setOpening(false); setAmount('') }} onRecoveryClose={() => setOpening(false)} dirty={Boolean(amount)} busy={cash.pending} recovery={cash.uncertain}>
      <div className="pos-section-body">
        {cash.error != null && <ApiErrorState error={cash.error} />}
        {cash.uncertain && <><Notice tone="warning">Apertura pendiente de verificar. No la repitas aunque no aparezca todavía una caja abierta.</Notice><Button disabled={cash.pending || cash.query.isFetching} onClick={() => void cash.refresh()}>Consultar resultado de apertura</Button></>}
        <form onSubmit={event => { event.preventDefault(); void cash.open(amount) }}>
            <TextField label="Monto inicial de caja (S/)" inputMode="decimal" value={amount} maxLength={32} required
              hint="Importe real de apertura, desde cero y hasta cuatro decimales. No se abrirá hasta que lo confirmes."
              error={amount && !valid ? 'Escribe un importe no negativo válido.' : undefined} onChange={event => setAmount(event.target.value)} disabled={cash.pending || cash.uncertain} />
            <FormFooter><Button type="submit" pending={cash.pending} disabled={!valid || cash.query.isFetching || cash.uncertain}>Confirmar apertura</Button></FormFooter>
          </form>
      </div>
    </Modal>
  </Panel>
}
