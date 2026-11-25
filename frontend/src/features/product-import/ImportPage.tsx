import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Steps } from '../../shared/ui/Steps'
import { InlineHelp } from '../../shared/ui/FormLayout'
import { importApi, maximumFileBytes, validFile } from './api'
import '../../shared/styles/operations.css'

export function ImportPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Esta sección requiere otro acceso</h1><p>Importar productos requiere Owner.</p></Panel>
  return <ImportWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function ImportWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient(); const [params, setParams] = useSearchParams(); const latest = useRef(params); useEffect(() => { latest.current = params }, [params])
  const jobId = params.get('importJob') ?? ''; const pending = params.has('importPending')
  const requestId = params.get('importPending') ?? ''
  const [recognized, setRecognized] = useState<{ requestId: string; jobId: string } | null>(null)
  // The URL restores a recovery address, never proof that an arbitrary job belongs to this intention.
  const legacyPending = pending && !isUuid(requestId)
  const acknowledged = pending && recognized?.requestId === requestId && recognized.jobId === jobId
  const awaitingReceipt = pending && !legacyPending && !acknowledged
  const [inputId, setInputId] = useState(jobId); const [file, setFile] = useState<File | null>(null); const [confirmed, setConfirmed] = useState(false); const [reviewed, setReviewed] = useState(false)
  const [cursors, setCursors] = useState([0]); const after = cursors.at(-1)!; const [busy, setBusy] = useState(false); const running = useRef(false); const abort = useRef<AbortController | null>(null)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState(''); const [denied, setDenied] = useState(false)
  const [recoveryReview, setRecoveryReview] = useState('')
  const lastObservedImport = useRef<{ id: string; importedRows: number } | null>(null)
  useEffect(() => () => abort.current?.abort(), [])
  function mark(id: string, blocked: boolean, requestId = latest.current.get('importPending') ?? '') { const next = new URLSearchParams(latest.current); for (const key of ['importJob', 'importPending', 'importAck']) next.delete(key); if (id) next.set('importJob', id); if (blocked) next.set('importPending', requestId); latest.current = next; setParams(next, { replace: true }) }
  const invalidate = () => client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === w.tenantId && ['catalog-products', 'catalog-detail', 'product-search'].includes(String(q.queryKey[3])) })
  const query = useQuery({ queryKey: scopedKey(w, 'product-import', w.session.userId, w.access.membershipId, jobId, after), queryFn: async ({ signal }) => {
    if (!http) throw new ApiError(0, 'client.https_required')
    const result = await importApi(http).read(w, jobId, after, signal)
    const previous = lastObservedImport.current
    if (!signal.aborted && (previous?.id !== result.importJobId || result.importedRows > previous.importedRows)) {
      // Paging may return an older snapshot. Refresh only for a new job or progress.
      const refresh = invalidate()
      lastObservedImport.current = { id: result.importJobId, importedRows: result.importedRows }
      await refresh
    }
    return result
  }, enabled: Boolean(http) && !awaitingReceipt && isUuid(jobId), retry: false, refetchOnWindowFocus: false })
  const job = !awaitingReceipt && query.isSuccess && !query.isFetching ? query.data : null
  const canRecoverInterrupted = job?.status === 'processing' && job.canRecoverInterrupted && !legacyPending && !query.isError
  const locked = busy || pending || denied || !http
  async function upload() {
    if (!http || running.current || latest.current.has('importPending') || denied || !file || !validFile(file) || !confirmed) return
    running.current = true; setBusy(true); setError(null); setNotice(''); setConfirmed(false); const requestId = crypto.randomUUID(); mark('', true, requestId); const controller = new AbortController(); abort.current = controller
    try {
      const result = await importApi(http).create(w, file, controller.signal, { id: requestId, branchId: w.branchId }); if (controller.signal.aborted) return
      setRecognized({ requestId, jobId: result.importJobId })
      mark(result.importJobId, true, requestId); setInputId(result.importJobId); setCursors([0]); setReviewed(false); setNotice('Trabajo reconocido. Su identificador queda en esta dirección para recargar y consultar; revisa el resultado persistido antes de otro archivo.'); await invalidate()
    } catch (failure) {
      if (controller.signal.aborted) return
      setError(failure); if (failure instanceof ApiError && !failure.outcomeUnknown) mark('', false)
      if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true)
      await invalidate()
    } finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  async function recover() {
    if (!http || running.current || !awaitingReceipt) return
    running.current = true; setBusy(true); setError(null); const controller = new AbortController(); abort.current = controller
    try { const original = await importApi(http).recover(w, requestId, controller.signal); if (!controller.signal.aborted) { setRecognized({ requestId, jobId: original.importJobId }); mark(original.importJobId, true, requestId); setInputId(original.importJobId); setCursors([0]); setReviewed(false); setNotice('Trabajo original localizado por su constancia. Consulta sus resultados; no se repitió el archivo.') } }
    catch (failure) { if (!controller.signal.aborted) setError(failure) }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  async function recoverInterrupted() {
    if (!http || running.current || !canRecoverInterrupted || recoveryReview !== jobId || denied) return
    running.current = true; setBusy(true); setRecoveryReview(''); setError(null); setNotice('')
    const controller = new AbortController(); abort.current = controller
    try {
      await importApi(http).recoverInterrupted(w, jobId, controller.signal)
      if (!controller.signal.aborted) setNotice('Estado terminal confirmado. Revisa las filas y los productos conservados.')
    } catch (failure) {
      if (!controller.signal.aborted) setError(failure)
    } finally {
      if (!controller.signal.aborted) {
        const observed = await query.refetch()
        if (!controller.signal.aborted) {
          if (observed.isSuccess && observed.data.status !== 'processing' && observed.data.completedAt) {
            setError(null); setNotice('Estado terminal confirmado. Revisa las filas y los productos conservados.')
          }
          setBusy(false)
        }
      }
      running.current = false
    }
  }
  async function download() {
    if (!http || running.current) return
    running.current = true; setBusy(true); setError(null); const controller = new AbortController(); abort.current = controller
    try { const blob = await importApi(http).template(w, controller.signal); if (controller.signal.aborted) return; const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'medipos-product-import-v1.xlsx'; document.body.append(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); setNotice('Plantilla oficial descargada. No se importó ningún producto.') }
    catch (failure) { if (!controller.signal.aborted) setError(failure) } finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  useNavigationProtection({ busy, pending })
  return <div className="operations"><PageHeader title="Importar productos desde Excel" description="Añade productos y su unidad base a tu botica. No ingresa stock ni publica en el catálogo global." />
    <Steps labels={['Descargar plantilla', 'Seleccionar archivo', 'Confirmar carga', 'Revisar resultado']} current={jobId || pending ? 3 : confirmed ? 2 : file ? 1 : 0} label="Importación de productos" />
    <Panel><div className="operation-body"><h2>Preparar archivo</h2><Button variant="secondary" disabled={busy || !http} onClick={() => void download()}>Descargar plantilla oficial XLSX</Button><InlineHelp title="Formato y límites de la plantilla">Plantilla v1: hoja Products, datos desde fila 4; máximo 5000 filas y {maximumFileBytes} bytes (5 MiB). Categorías activas y datos farmacéuticos documentados. La unidad base se crea con el producto; no ingresa stock.</InlineHelp>
      <TextField label="Archivo Excel .xlsx" type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" disabled={locked} onChange={e => { setFile(e.target.files?.[0] ?? null); setConfirmed(false) }} />
      {file && <p>{file.name} · {file.size} bytes{!validFile(file) ? ' · Archivo inválido: requiere XLSX, nombre sin rutas y entre 1 byte y 5 MiB.' : ''}</p>}
      <Notice tone="warning">Importar persiste productos y su unidad base. Las filas aceptadas se confirman individualmente; un trabajo fallido puede contener productos ya importados. No hay idempotencia por archivo, nombre o referencia. No repitas el envío ante pérdida de respuesta.</Notice>
      <label><input type="checkbox" checked={confirmed} disabled={locked || !file || !validFile(file)} onChange={e => setConfirmed(e.target.checked)} /> Confirmo crear los productos comerciales válidos de este archivo en {w.tenantName}.</label><Button disabled={locked || !confirmed || !file || !validFile(file)} onClick={() => void upload()}>Importar archivo confirmado</Button>
      {busy && <p role="status">Procesando solicitud…</p>}{error != null && <ApiErrorState error={error} />}{notice && <p role="status">{notice}</p>}
      {pending && <Notice tone="warning">Importación pendiente de revisión; no se reenviará. {legacyPending ? 'Esta solicitud anterior carece de una clave recuperable. Puedes consultar trabajos existentes. Conserva el bloqueo y solicita a soporte que verifique la solicitud original.' : awaitingReceipt ? 'La respuesta no identificó el trabajo. Localízalo por su constancia antes de revisar las filas.' : 'Consulta el trabajo original y verifica su finalización.'}</Notice>}
    </div></Panel>
    <Panel><div className="operation-body"><h2>Consultar trabajo persistido</h2>{awaitingReceipt ? <Button disabled={busy || !http} onClick={() => void recover()}>Localizar trabajo por su intención original</Button> : <form onSubmit={e => { e.preventDefault(); if (!busy && isUuid(inputId) && (!acknowledged || inputId === jobId)) { mark(inputId, pending); setCursors([0]); setReviewed(false); if (inputId === jobId && after === 0) void query.refetch() } }}><TextField label={acknowledged ? 'Identificador del trabajo original' : 'Identificador del trabajo a consultar'} value={inputId} readOnly={acknowledged} onChange={e => setInputId(e.target.value.trim())} /><Button type="submit" disabled={busy || !http || !isUuid(inputId)}>Consultar trabajo</Button></form>}
      {query.isFetching && <p role="status">Consultando resultado…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} />}
      {job && <><h3>Resultado: {job.status === 'processing' ? 'En proceso' : job.status === 'completed' ? 'Completado' : 'Fallido'}</h3><p>Archivo: {job.fileName} · Plantilla {job.templateVersion} · Creado UTC {job.createdAt} · Finalizado UTC {job.completedAt ?? 'Pendiente'}</p><dl className="summary-grid"><div><dt>Filas totales</dt><dd>{job.totalRows}</dd></div><div><dt>Válidas</dt><dd>{job.validRows}</dd></div><div><dt>Importadas</dt><dd>{job.importedRows}</dd></div><div><dt>Inválidas</dt><dd>{job.invalidRows}</dd></div></dl>
        <div className="operation-table"><table><caption>Resultados persistidos por fila</caption><thead><tr><th>Fila</th><th>Estado</th><th>Producto creado</th><th>Código</th><th>Error</th></tr></thead><tbody>{job.rows.map(row => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.status}</td><td>{row.businessProductId ?? 'No creado'}</td><td>{row.errorCode ?? '—'}</td><td>{row.errorMessage ?? '—'}</td></tr>)}</tbody></table></div>{!job.rows.length && <p>No hay filas en esta página del trabajo.</p>}
        <Button disabled={busy || cursors.length < 2} onClick={() => setCursors(cursors.slice(0, -1))}>Resultados anteriores</Button><Button disabled={busy || job.nextAfterRowNumber === null} onClick={() => { if (job.nextAfterRowNumber !== null) setCursors([...cursors, job.nextAfterRowNumber]) }}>Resultados siguientes</Button>
        {acknowledged && job.status !== 'processing' && job.completedAt && <><label><input type="checkbox" checked={reviewed} onChange={e => setReviewed(e.target.checked)} /> Verifiqué que es el trabajo de la solicitud original y que terminó; revisé sus productos importados, incluidos los parciales si falló.</label><Button disabled={!reviewed || busy} onClick={() => { mark(job.importJobId, false); setRecognized(null); setFile(null); setConfirmed(false); setReviewed(false); setError(null); setNotice('Trabajo original conciliado. No se repitió el envío. Puedes preparar otro archivo distinto.') }}>Finalizar revisión de importación</Button></>}
      </>}
      {canRecoverInterrupted && <fieldset disabled={busy || denied}><legend>Recuperar importación interrumpida</legend>
        <label><input type="checkbox" checked={recoveryReview === jobId} onChange={event => setRecoveryReview(event.target.checked ? jobId : '')} /> Confirmo cerrar este trabajo y conservar sus filas y productos registrados.</label>
        <Button type="button" disabled={recoveryReview !== jobId} onClick={() => void recoverInterrupted()}>Cerrar trabajo interrumpido</Button>
      </fieldset>}
      {jobId && !awaitingReceipt && <Button disabled={busy || query.isFetching} onClick={() => void query.refetch()}>Actualizar mismo trabajo</Button>}
    </div></Panel></div>
}
