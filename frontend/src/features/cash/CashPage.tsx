import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { Modal } from '../../shared/ui/Modal'
import { FormFooter } from '../../shared/ui/FormLayout'
import { scopedKey } from '../../shared/api/contracts'
import { decimalInput, decimalLabel, maximumMoney } from '../../shared/api/decimal'
import { cashApi } from './api'
import { useCashSession } from './useCashSession'
import { useCashClose } from './useCashClose'
import { CashSessionPanel } from './CashSessionPanel'

export function CashPage() {
  const w = useWorkspace()
  return <CashWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CashWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime()
  const cash = useCashSession(); const close = useCashClose()
  const [amount, setAmount] = useState(''); const [reviewed, setReviewed] = useState(false); const [offset, setOffset] = useState(0)
  const [closing, setClosing] = useState(false)
  const active = useQuery({ queryKey: scopedKey(w, 'active-cash', w.session.userId, w.access.membershipId, offset),
    queryFn: ({ signal }) => cashApi(http!).active(w, offset, signal), enabled: Boolean(http && w.access.role === 'owner'), retry: false, staleTime: 0,
  })
  const pick = (id: string) => { setAmount(''); setReviewed(false); setClosing(true); void close.consult(id) }
  const valid = decimalInput(amount, maximumMoney, 4, true)
  const canClose = !close.denied && close.status?.status === 'open' && (w.access.role === 'owner' || close.status.membershipId === w.access.membershipId)
  return <>
    <PageHeader title="Caja" description={`Apertura, cierre y arqueo · ${w.branch.name}`} />
    {close.pending ? <Notice tone="warning">Cierre pendiente de verificar. Consulta esta misma caja; no se enviará otro cierre ni se ofrecerá una apertura nueva.</Notice> : <CashSessionPanel cash={cash} />}
    <Panel className="pos-cash"><div className="pos-section-body">
      <h2>Cierre y arqueo</h2>
      {close.error != null && <ApiErrorState error={close.error} title="Revisa el estado de la caja" />}
      <Button variant="secondary" disabled={!cash.session || close.busy || close.pending} onClick={() => { if (cash.session) pick(cash.session.cashSessionId) }}>Preparar cierre de mi caja</Button>
      {close.id && <Button variant="quiet" disabled={close.busy} onClick={() => { setClosing(true); void close.consult() }}>Consultar cierre guardado</Button>}
      {close.busy && <p role="status">Consultando caja…</p>}
      <Modal open={closing} title="Arqueo y cierre de caja" onClose={() => { setClosing(false); setAmount(''); setReviewed(false) }} onRecoveryClose={() => setClosing(false)} dirty={Boolean(canClose && (amount || reviewed))} busy={close.busy} recovery={close.pending}>
        <div className="pos-section-body">
        {close.error != null && <ApiErrorState error={close.error} title="Revisa el resultado del cierre" />}
        {close.pending && <><Notice tone="warning">Cierre pendiente de verificar. No se enviará otra solicitud.</Notice><Button disabled={close.busy} onClick={() => void close.consult()}>Consultar esta misma caja</Button></>}
      {canClose && !close.pending && <form aria-label="Cierre de caja" onSubmit={event => { event.preventDefault(); if (reviewed) void close.close(close.status!.cashSessionId, amount) }}>
        <p>Cuenta el efectivo físico. El servidor calculará ventas netas, movimientos de sencillo, esperado y diferencia al cerrar. El cierre no anula ventas.</p>
        <TextField label="Efectivo contado (S/)" value={amount} inputMode="decimal" maxLength={32} required disabled={close.busy} onChange={event => { setAmount(event.target.value); setReviewed(false) }} />
        <label><input type="checkbox" checked={reviewed} disabled={close.busy} onChange={event => setReviewed(event.target.checked)} /> Revisé el conteo y autorizo cerrar esta caja.</label>
        <FormFooter><Button type="submit" pending={close.busy} disabled={!valid || !reviewed}>Confirmar cierre de caja</Button></FormFooter>
      </form>}
      {close.status?.status === 'closed' && <p role="status">Caja cerrada en el servidor. Consultar su arqueo no repite el cierre.</p>}
      {close.receipt && <section aria-label="Arqueo registrado">
        <h3>Arqueo registrado · {close.receipt.employeeBranch.displayName}</h3>
        <p>Apertura: S/ {decimalLabel(close.receipt.openingAmount, 2)} · Ventas netas: S/ {decimalLabel(close.receipt.netSalesAmount, 2)}</p>
        <ul>{Object.entries(close.receipt.paymentTotals).map(([method, value]) => <li key={method}>{({ cash: 'Efectivo', yape: 'Yape', plin: 'Plin', card: 'Tarjeta', transfer: 'Transferencia' })[method]}: S/ {decimalLabel(value, 2)}</li>)}</ul>
        <p>Sencillo recibido: S/ {decimalLabel(close.receipt.cashTransfers.incoming, 2)} · Enviado: S/ {decimalLabel(close.receipt.cashTransfers.outgoing, 2)}</p>
        <p>Esperado: S/ {decimalLabel(close.receipt.expectedCashAmount, 2)} · Contado: S/ {decimalLabel(close.receipt.countedCashAmount, 2)} · Diferencia: S/ {decimalLabel(close.receipt.cashDifference, 2)}</p>
      </section>}
        </div>
      </Modal>
    </div></Panel>
    {w.access.role === 'owner' && <Panel className="pos-cash"><div className="pos-section-body"><h2>Cajas activas en esta sucursal</h2>
      {active.isPending && <p>Consultando cajas activas…</p>}
      {active.isError && <ApiErrorState error={active.error} title="No pudimos consultar las cajas activas" />}
      {active.data && !active.isError && <>{!active.data.length && <p>No hay cajas en esta página.</p>}
        <ul>{active.data.map(row => <li key={row.cashSessionId}>{row.displayName} · Ventas acumuladas S/ {decimalLabel(row.accumulatedSales, 2)} <Button variant="quiet" disabled={close.busy || close.pending} onClick={() => pick(row.cashSessionId)}>Revisar caja de {row.displayName}</Button></li>)}</ul></>}
      <Button variant="quiet" disabled={active.isFetching} onClick={() => void active.refetch()}>Actualizar cajas activas</Button>
      <Button variant="quiet" disabled={offset === 0 || active.isFetching} onClick={() => setOffset(offset - 50)}>Anterior</Button>
      <Button variant="quiet" disabled={active.isFetching || !active.data || active.data.length < 50 || offset >= 100000} onClick={() => setOffset(offset + 50)}>Siguiente</Button>
    </div></Panel>}
  </>
}
