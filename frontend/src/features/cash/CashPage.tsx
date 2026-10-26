import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { Banknote } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { Drawer, Modal } from '../../shared/ui/Modal'
import { FormFooter } from '../../shared/ui/FormLayout'
import { scopedKey } from '../../shared/api/contracts'
import { decimalInput, decimalLabel, maximumMoney } from '../../shared/api/decimal'
import { cashApi } from './api'
import { useCashSession } from './useCashSession'
import type { CashController } from './useCashSession'
import { useCashClose } from './useCashClose'
import { CashSessionPanel } from './CashSessionPanel'
import { CashAge } from './CashAge'
import { MetricGrid } from '../../shared/ui/MetricCard'
import { BarChart } from '../../shared/ui/BarChart'
import { CashDifference } from './CashDifference'

export function CashPage() {
  const w = useWorkspace()
  return <CashWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CashWorkspace() {
  const w = useWorkspace(); const cash = useCashSession()
  return <><PageHeader icon={Banknote} title="Arqueos y cajas" description={w.branch.name} /><CashManagement cash={cash} /></>
}
export function CashManagement({ cash, embedded = false, blocked = false }: { cash: CashController; embedded?: boolean; blocked?: boolean }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const close = useCashClose()
  const [amount, setAmount] = useState(''); const [reviewed, setReviewed] = useState(false); const [offset, setOffset] = useState(0)
  const [closing, setClosing] = useState(false)
  const [managing, setManaging] = useState(false)
  const active = useQuery({ queryKey: scopedKey(w, 'active-cash', w.session.userId, w.access.membershipId, offset),
    queryFn: ({ signal }) => cashApi(http!).active(w, offset, signal), enabled: Boolean(http && w.access.role === 'owner' && !embedded), retry: false, staleTime: 0,
  })
  const pick = (id: string) => { setAmount(''); setReviewed(false); setManaging(false); setClosing(true); void close.consult(id) }
  const valid = decimalInput(amount, maximumMoney, 4, true)
  const canClose = !close.denied && close.status?.status === 'open' && (w.access.role === 'owner' || close.status.membershipId === w.access.membershipId)
  return <>
    {close.pending ? <Notice tone="warning">Cierre por conciliar. Consulta esta misma caja; no se enviará otro cierre.</Notice> : <CashSessionPanel cash={cash} compact={embedded} blocked={blocked} />}
    <Panel className={embedded ? 'cash-management-embedded' : 'pos-cash'}><div className="pos-section-body">
      {!embedded && <h2>Cierre y arqueo</h2>}
      {close.error != null && <ApiErrorState error={close.error} title="Revisa el estado de la caja" />}
      {embedded ? cash.session && <Button variant="quiet" disabled={close.busy || close.pending || blocked} onClick={() => setManaging(true)}>Gestionar caja</Button> : <Button variant="secondary" disabled={!cash.session || close.busy || close.pending || blocked} onClick={() => { if (cash.session) pick(cash.session.cashSessionId) }}>Preparar cierre de mi caja</Button>}
      {close.id && <Button data-recovery-focus variant="quiet" disabled={close.busy} onClick={() => { setClosing(true); void close.consult() }}>Consultar cierre guardado</Button>}
      {close.busy && <p role="status">Consultando caja…</p>}
      <Modal open={closing} title="Cerrar y cuadrar caja" onClose={() => { setClosing(false); setAmount(''); setReviewed(false) }} onRecoveryClose={() => setClosing(false)} dirty={Boolean(canClose && (amount || reviewed))} busy={close.busy} recovery={close.pending}>
        <div className="pos-section-body">
        {close.error != null && <ApiErrorState error={close.error} title="Revisa el resultado del cierre" />}
        {close.pending && <><Notice tone="warning">Cierre pendiente de verificar. No se enviará otra solicitud.</Notice><Button disabled={close.busy} onClick={() => void close.consult()}>Consultar esta misma caja</Button></>}
      {canClose && !close.pending && <form aria-label="Cierre de caja" onSubmit={event => { event.preventDefault(); if (reviewed && !blocked) void close.close(close.status!.cashSessionId, amount) }}>
        <p className="data-caption">El efectivo esperado y la diferencia se obtienen del servidor al cerrar. Yape, Plin y tarjetas no son efectivo físico.</p>
        <TextField label="Efectivo físico contado (S/)" value={amount} inputMode="decimal" maxLength={32} required disabled={close.busy || blocked} onChange={event => { setAmount(event.target.value); setReviewed(false) }} />
        <label><input type="checkbox" checked={reviewed} disabled={close.busy} onChange={event => setReviewed(event.target.checked)} /> Revisé el conteo y autorizo cerrar esta caja.</label>
        <FormFooter><Button type="submit" pending={close.busy} disabled={!valid || !reviewed || blocked}>Confirmar cierre de caja</Button></FormFooter>
      </form>}
      {close.status?.status === 'closed' && <p role="status">Caja cerrada en el servidor. Consultar su arqueo no repite el cierre.</p>}
      {close.status?.status === 'open' && <CashAge key={close.status.cashSessionId} openedAt={close.status.openedAt} />}
      {close.receipt && <section aria-label="Arqueo registrado">
        <h3>Arqueo registrado · {close.receipt.employeeBranch.displayName}</h3>
        <MetricGrid metrics={[
          ['Efectivo esperado', `S/ ${decimalLabel(close.receipt.expectedCashAmount, 2)}`], ['Efectivo contado', `S/ ${decimalLabel(close.receipt.countedCashAmount, 2)}`],
        ]} />
        <CashDifference value={close.receipt.cashDifference} />
        <details className="detail-disclosure"><summary>Ver pagos y movimientos del arqueo</summary>
        <BarChart title="Pagos de esta caja cerrada" description="Totales por medio de pago del arqueo consultado. No son un reporte de todas las ventas del negocio." rows={Object.entries(close.receipt.paymentTotals).map(([method, value]) => ({ key: method, label: ({ cash: 'Efectivo', yape: 'Yape', plin: 'Plin', card: 'Tarjeta', transfer: 'Transferencia' })[method] ?? method, value, formatted: `S/ ${decimalLabel(value, 2)}`, tone: 'info' }))} />
        <p>Apertura: S/ {decimalLabel(close.receipt.openingAmount, 2)} · Ventas netas: S/ {decimalLabel(close.receipt.netSalesAmount, 2)}</p>
        <p>Sencillo recibido: S/ {decimalLabel(close.receipt.cashTransfers.incoming, 2)} · Enviado: S/ {decimalLabel(close.receipt.cashTransfers.outgoing, 2)}</p>
        </details>
      </section>}
        </div>
      </Modal>
    </div></Panel>
    <Drawer open={managing} title="Gestionar mi caja" description={w.branch.name} onClose={() => setManaging(false)} busy={close.busy}>
      <div className="operation-body">{cash.session && <><p>Sesión de caja de tu membresía · inicial S/ {decimalLabel(cash.session.openingAmount, 2)}</p><CashAge openedAt={cash.session.openedAt} /><Button variant="primary" disabled={close.busy || close.pending || blocked} onClick={() => pick(cash.session!.cashSessionId)}>Cerrar y cuadrar caja</Button></>}
        <Button variant="secondary" disabled={cash.pending || cash.query.isFetching} onClick={() => void cash.refresh()}>Actualizar estado</Button>
        {!close.pending && <Link className="text-link" to={w.base + '/caja'}>Consultar arqueos y cajas autorizadas</Link>}
      </div>
    </Drawer>
    {!embedded && w.access.role === 'owner' && <Panel className="pos-cash"><div className="pos-section-body"><h2>Cajas activas en esta sucursal</h2>
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
