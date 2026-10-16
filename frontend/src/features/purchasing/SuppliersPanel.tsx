import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { Button } from '../../shared/ui/Button'
import { PageControls } from '../../shared/ui/PageControls'
import { purchasingApi } from './api'
import type { Supplier } from './api'

export function SuppliersPanel({ selected, select, disabled, onBusyChange, receiptSupplierId }: { selected: Supplier | null; select: (supplier: Supplier | null) => void; disabled: boolean; onBusyChange: (busy: boolean) => void; receiptSupplierId?: string | null }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0)
  const [params, setParams] = useSearchParams(); const pending = params.has('supplierPending')
  const [name, setName] = useState(''); const [ruc, setRuc] = useState(''); const [contact, setContact] = useState('')
  const [busy, setBusy] = useState(false); const running = useRef(false); const [error, setError] = useState<unknown>(null)
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null)
  const [created, setCreated] = useState<Supplier | null>(null); const [denied, setDenied] = useState(false)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  const rows = useQuery({ queryKey: scopedKey(w, 'suppliers', w.session.userId, w.access.membershipId, offset),
    queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return purchasingApi(http).suppliers(w, offset, 20, signal) },
    enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  function mark(value: boolean) { setParams(current => { const next = new URLSearchParams(current); if (value) next.set('supplierPending', '1'); else next.delete('supplierPending'); return next }, { replace: true }) }
  async function create() {
    if (!http || running.current || pending || disabled || denied || w.access.role !== 'owner' || !name.trim() || name.trim().length > 256) return
    running.current = true; setBusy(true); onBusyChange(true); setError(null); setCreated(null); mark(true); controller.current = new AbortController()
    try {
      const result = await purchasingApi(http).createSupplier(w, { name: name.trim(), ruc: ruc.trim() || null, contact: contact.trim() || null }, controller.current.signal)
      if (!mounted.current) return
      setCreated(result); select(result); mark(false); setName(''); setRuc(''); setContact(''); await rows.refetch()
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && !failure.outcomeUnknown) { mark(false); if (failure.status === 401 || failure.status === 403) setDenied(true) }
    } finally { running.current = false; if (mounted.current) { setBusy(false); onBusyChange(false) } }
  }
  return <Panel><div className="operation-body"><h2>Proveedores</h2>
    <p>Proveedor opcional para la recepción. Los datos deben ser documentados; RUC y contacto pueden quedar vacíos.</p>
    {rows.isPending && <p>Consultando proveedores…</p>}
    {rows.isError && <ApiErrorState error={rows.error} title="No pudimos consultar proveedores" />}
    {rows.data && !rows.isError && <ul>{!rows.data.length && <li>No hay proveedores en esta página.</li>}{rows.data.map(row => <li key={row.id}>{row.name} · RUC {row.ruc ?? 'No informado'} · {row.contact ?? 'Sin contacto'} · {row.isActive ? 'Activo' : 'Inactivo'}{row.id === receiptSupplierId && <strong> · Proveedor de la recepción consultada</strong>} <Button variant="quiet" disabled={disabled || busy || !row.isActive} onClick={() => select(row)}>Elegir {row.name}</Button></li>)}</ul>}
    <PageControls offset={offset} count={rows.isError ? undefined : rows.data?.length} busy={rows.isFetching} change={setOffset} refresh={() => void rows.refetch()} />
    <p>Proveedor elegido para una nueva recepción: {selected?.name ?? 'Ninguno'}</p><Button variant="quiet" disabled={disabled || busy} onClick={() => select(null)}>Continuar sin proveedor</Button>
    {error != null && <ApiErrorState error={error} title="No pudimos crear el proveedor" />}
    {created && <p role="status">Proveedor creado mediante la API: {created.name}. La lista puede requerir otra página.</p>}
    {pending && <p role="alert">Alta de proveedor pendiente de conciliación. No repitas el POST: revisa las páginas y compara nombre, RUC, contacto y fecha con la solicitud original; una ausencia en esta página no demuestra que falló. Se requiere conciliación operacional antes de otra alta.</p>}
    {w.access.role === 'owner' && <form aria-label="Crear proveedor" onSubmit={event => { event.preventDefault(); void create() }}><fieldset disabled={disabled || busy || pending || denied || !http}><legend>Nuevo proveedor · solo Owner</legend>
      <TextField label="Nombre del proveedor" required value={name} maxLength={256} onChange={event => setName(event.target.value)} />
      <TextField label="RUC del proveedor (opcional)" value={ruc} maxLength={32} onChange={event => setRuc(event.target.value)} />
      <TextField label="Contacto (opcional)" value={contact} maxLength={512} onChange={event => setContact(event.target.value)} />
      <Button type="submit" disabled={!name.trim()} pending={busy}>Crear proveedor una vez</Button>
    </fieldset></form>}
  </div></Panel>
}
