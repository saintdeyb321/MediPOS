import { useEffect, useEffectEvent, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ArrowLeft, ClipboardList } from 'lucide-react'
import { ApiErrorState, useWorkspace } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Notice } from '../../shared/ui/Notice'
import { TextField } from '../../shared/ui/TextField'
import { DataSection } from '../../shared/ui/DataSection'
import { EditorLayout } from '../../shared/ui/EditorLayout'
import { HelpPopover } from '../../shared/ui/HelpPopover'
import { Badge } from '../../shared/ui/Badge'
import { decimalLabel } from '../../shared/api/decimal'
import { receiptTotal } from './receipt'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { SuppliersPanel } from './SuppliersPanel'
import { ReceiptEditor } from './ReceiptEditor'
import { PurchaseReview } from './PurchaseReview'
import { PurchaseLookup } from './PurchaseLookup'
import { usePurchaseReceipt } from './usePurchaseReceipt'
import type { Supplier } from './api'
import '../../shared/styles/operations.css'

export function PurchasesPage() {
  const w = useWorkspace()
  if (!['owner', 'pharmacist'].includes(w.access.role)) return <Notice tone="warning">Compras requiere Owner o Pharmacist con acceso operacional vigente.</Notice>
  return <PurchasesWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function PurchasesWorkspace() {
  const w = useWorkspace(); const receipt = usePurchaseReceipt()
  const [parameters] = useSearchParams()
  const [supplier, setSupplier] = useState<Supplier | null>(null); const [reference, setReference] = useState('')
  const [supplierBusy, setSupplierBusy] = useState(false)
  const [edit, setEdit] = useState(false); const [reconciled, setReconciled] = useState(false)
  const [starting, setStarting] = useState(() => parameters.get('nueva') === '1')
  const [directory, setDirectory] = useState(false)
  const [newRequested, setNewRequested] = useState(false)
  const view = receipt.server?.purchase.id.toLowerCase() === receipt.id.toLowerCase() ? receipt.server : null
  const hasEditor = starting || Boolean(receipt.id)
  const supplierPending = parameters.has('supplierPending')
  const editor = Boolean(receipt.pending || supplierPending || (!directory && hasEditor))
  const revisit = useEffectEvent(() => {
    if (receipt.id && receipt.error == null && !receipt.busy && !receipt.pending && !edit && view?.lines.length) void receipt.consult()
  })
  useEffect(() => {
    const onVisible = () => { if (document.visibilityState === 'visible') revisit() }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [])
  function startNew() {
    receipt.startNew(); setStarting(true); setDirectory(false); setEdit(false); setReconciled(false); setReference(''); setSupplier(null); setNewRequested(false)
  }
  const canLeave = !receipt.busy && !supplierBusy
  const unsaved = edit || Boolean(view && view.purchase.status === 'draft' && !view.lines.length) || (starting && !receipt.id && Boolean(reference || supplier))
  const legacySearch = Boolean(receipt.pending && !receipt.creationPending && !receipt.id)
  return <div className="operations">
    {editor && !receipt.pending && !supplierPending && <Button variant="quiet" disabled={!canLeave} onClick={() => setDirectory(true)}><ArrowLeft size={16} aria-hidden="true" />Volver a compras</Button>}
    <PageHeader icon={ClipboardList} eyebrow="" focusOnChange title={editor ? receipt.id ? 'Recepción' : 'Nueva recepción' : 'Compras'}
      action={!editor && <>
        {parameters.get('origen') === 'inventario' && canLeave && !unsaved && <Link className="button secondary" to={w.base + '/inventario'}>Volver a inventario</Link>}
        <Button disabled={receipt.locked || supplierBusy} onClick={() => { if (unsaved || (receipt.id && !view)) setNewRequested(true); else startNew() }}>Nueva recepción</Button>
      </>} />
    <ConfirmDialog open={newRequested} title="Preparar otra recepción" confirmLabel="Continuar con otra recepción" disabled={receipt.locked || supplierBusy} onClose={() => setNewRequested(false)} onConfirm={startNew}><p>Las líneas que aún no guardaste se descartarán. La recepción ya persistida conserva su estado y no se elimina ni se confirma.</p></ConfirmDialog>
    {receipt.error != null && <ApiErrorState error={receipt.error} title="No pudimos completar la operación de compra" retry={receipt.id && !receipt.pending && !edit ? () => void receipt.consult() : undefined} />}
    {receipt.notice && <p role="status">{receipt.notice}</p>}
    {receipt.creationPending && <Panel><div className="operation-body"><h2>Recepción pendiente de conciliación</h2>
      <p>Consulta la constancia para recuperar el borrador original. Si todavía no aparece, reconsulta más tarde.</p>
      <Button variant="secondary" disabled={receipt.busy} onClick={() => void receipt.recoverCreation()}>Consultar constancia del alta</Button>
    </div></Panel>}
    {receipt.pending && !receipt.creationPending && <Panel><div className="operation-body"><h2>Recepción pendiente de conciliación</h2><p>No pudimos confirmar el resultado. Consulta la recepción original antes de repetir la operación.</p>
    {receipt.id && <Button variant="secondary" disabled={receipt.busy} onClick={() => { setReconciled(false); void receipt.consult() }}>Consultar estado de la misma recepción</Button>}
      {view && receipt.pending !== 'confirm' && <><label><input type="checkbox" checked={reconciled} onChange={event => setReconciled(event.target.checked)} /> Verifiqué el registro original, todas sus líneas y la finalización de la solicitud; no queda otra escritura en curso.</label>
        <Button variant="secondary" disabled={!reconciled || receipt.busy} onClick={() => { receipt.acknowledge(); setReconciled(false) }}>Continuar tras conciliación verificada</Button></>}
      {receipt.pending === 'confirm' && <p>Si todavía aparece borrador, la confirmación puede seguir en curso. Solo una lectura confirmada libera este bloqueo; no se vuelve a enviar el POST.</p>}
    </div></Panel>}
    <div hidden={editor && !legacySearch}>
      <DataSection toolbar={directory && hasEditor && <div className="operation-actions"><Button variant="secondary" onClick={() => setDirectory(false)}>Retomar recepción</Button></div>}>
        <PurchaseLookup busy={receipt.busy || unsaved} fixedId={receipt.pending ? receipt.id : ''} consult={id => {
          setDirectory(false); setEdit(false); setReconciled(false); void receipt.consult(id)
        }} />
      </DataSection>
    </div>
    <div hidden={!editor || (!hasEditor && !supplierPending)}>
      <EditorLayout summary={view && <Panel><div className="operation-body">
        <h2>Resumen de recepción</h2><Badge tone={receipt.pending ? 'warning' : view.purchase.status === 'confirmed' ? 'success' : 'info'}>{receipt.pending ? 'Por conciliar' : view.purchase.status === 'confirmed' ? 'Confirmada' : 'Borrador'}</Badge>
        <dl className="summary-grid"><div><dt>Referencia</dt><dd>{view.purchase.documentReference ?? 'Sin referencia'}</dd></div><div><dt>Líneas guardadas</dt><dd>{view.lines.length}</dd></div><div><dt>Importe guardado</dt><dd>S/ {decimalLabel(receiptTotal(view.lines), 2)}</dd></div></dl>
      </div></Panel>}>
    {(starting || receipt.id || supplierPending) && <SuppliersPanel selected={supplier} select={setSupplier} disabled={receipt.locked || Boolean(receipt.id)} onBusyChange={setSupplierBusy} receiptSupplierId={view?.purchase.supplierId} />}
    {starting && !receipt.id && !receipt.pending && <Panel><form className="operation-body" aria-label="Nueva recepción" onSubmit={event => { event.preventDefault(); setEdit(false); void receipt.create(supplier?.id ?? null, reference.trim() || null) }}><h2>Referencia y borrador</h2>
      <TextField label="Referencia del comprobante (opcional)" value={reference} maxLength={256} onChange={event => setReference(event.target.value)} hint="Te ayudará a encontrar esta recepción." />
      <div className="operation-actions"><Button type="submit" disabled={receipt.locked || supplierBusy}>Continuar con productos y lotes</Button>
        <HelpPopover label="Sobre el borrador"><p>El stock ingresa al confirmar la recepción. Preparar el borrador no registra existencias.</p></HelpPopover>
      </div>
    </form></Panel>}
    {view && <Panel><div className="operation-body"><div hidden={view.purchase.status === 'draft' && (edit || !view.lines.length)}><PurchaseReview key={view.purchase.id} view={view} disabled={receipt.locked || edit} confirm={() => void receipt.confirm()} /></div>
      {view.purchase.status === 'draft' && view.lines.length > 0 && !edit && <Button variant="secondary" disabled={receipt.locked} onClick={() => setEdit(true)}>Editar conjunto de líneas del borrador</Button>}
      {view.purchase.status === 'draft' && (view.lines.length === 0 || edit) && <ReceiptEditor key={view.purchase.id} disabled={receipt.locked} replacing={view.lines.length > 0} save={lines => { void receipt.save(lines)?.then(saved => { if (saved) setEdit(false) }) }} />}
    </div></Panel>}
      </EditorLayout>
    </div>
  </div>
}
