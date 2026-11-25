import { useState } from 'react'
import { Link } from 'react-router'
import { ArrowRight, Building2, KeyRound, Layers3, ShieldCheck, UserRoundCheck } from 'lucide-react'
import { LogoutButton } from '../auth'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { DateText } from '../../shared/ui/DateText'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { TextField } from '../../shared/ui/TextField'
import { PlatformReadError } from './PlatformAdmissionError'
import { categoryNameKey } from './usePlatformConsole'
import { expiresSoon } from './presentation'
import type { PlatformConsoleState } from './PlatformTenantDirectory'

export function PlatformHome({ p, destination }: { p: PlatformConsoleState; destination: (area: string) => string }) {
  const rows = p.list.data?.items
  const metrics = rows && [
    { label: 'Boticas', count: rows.length, icon: Building2 },
    { label: 'Propietarios pendientes', count: rows.filter(row => row.administration?.ownerStatus === 'pending').length, icon: UserRoundCheck },
    { label: 'Licencias por vencer en 7 días', count: rows.filter(row => row.administration && expiresSoon(row.administration.expiresAt, p.list.dataUpdatedAt)).length, icon: KeyRound },
  ]
  return <>
    <PlatformReadError error={p.list.error} failedAt={p.list.errorUpdatedAt} fetching={p.list.isFetching} retry={p.admitted ? () => void p.list.refetch() : undefined} />
    {p.list.isFetching && <p role="status">{rows ? 'Actualizando indicadores…' : 'Consultando indicadores…'}</p>}
    {metrics && <section aria-label="Indicadores de la primera página">
      <p className="platform-note">Primera página del directorio, hasta 20 boticas.{(p.list.isError || !p.admitted) && ' Datos de la última lectura.'}</p>
      <dl className="platform-kpis">{metrics.map(({ label, count, icon: Icon }) =>
        <div key={label}><Icon aria-hidden="true" size={21} /><dt>{label}</dt><dd>{count}</dd></div>)}</dl>
      {rows?.length === 0 && <Notice>Crea la primera botica para que su propietario pueda reclamar el acceso con Google.</Notice>}
    </section>}
    <section className="platform-shortcuts" aria-label="Accesos directos de plataforma">
      {[
        { area: 'businesses', label: 'Ver boticas', icon: Building2 },
        { area: 'owners', label: 'Revisar asignaciones', icon: UserRoundCheck },
        { area: 'licenses', label: 'Gestionar licencias', icon: KeyRound },
      ].map(({ area, label, icon: Icon }) => <Link key={area} replace to={destination(area)}><Icon size={19} aria-hidden="true" />{label}<ArrowRight size={16} aria-hidden="true" /></Link>)}
    </section>
  </>
}

export function PlatformCategories({ p, create }: { p: PlatformConsoleState; create: () => void }) {
  const [filter, setFilter] = useState('')
  const rows = p.categories.data
  const visible = rows?.filter(row => categoryNameKey(row.name).includes(categoryNameKey(filter)))
  const lastRead = p.categories.isError || !p.admitted
  return <section className="platform-categories" aria-label="Categorías globales">
    <Panel><div className="operation-body">
      <div className="platform-context">
        <TextField label="Buscar categoría" value={filter} onChange={event => setFilter(event.target.value)} />
        <div className="platform-account-actions">
          {!p.categories.isError && p.admitted && <Button variant="secondary" disabled={p.busy || p.categories.isFetching || Boolean(p.categoryPending)} onClick={() => void p.categories.refetch()}>Actualizar categorías</Button>}
          <Button disabled={p.locked || !p.categoriesRead || !rows || rows.length >= 100} onClick={create}>Nueva categoría</Button>
        </div>
      </div>
      {rows && <p className="platform-note">{rows.length} {rows.length === 1 ? 'categoría consultada' : 'categorías consultadas'}.{lastRead && ' Datos de la última lectura.'}</p>}
      {rows && rows.length >= 100 && <Notice tone="warning">Esta consulta muestra hasta 100 categorías. Revisa el catálogo completo antes de crear otra.</Notice>}
    </div></Panel>
    <PlatformReadError error={p.categories.error} failedAt={p.categories.errorUpdatedAt} fetching={p.categories.isFetching} retry={p.admitted && !p.categoryPending ? () => void p.categories.refetch() : undefined} />
    {p.categories.isFetching && (rows ? <p role="status">Actualizando categorías…</p> : <LoadingState label="Cargando categorías…" />)}
    {rows?.length === 0 && <EmptyState title="No hay categorías activas." description="Crea una referencia compartida para organizar el catálogo." />}
    {Boolean(rows?.length) && visible?.length === 0 && <EmptyState title="Sin coincidencias" description="Prueba con otro nombre dentro de las categorías consultadas." />}
    {Boolean(visible?.length) && <ul className="platform-category-grid">{visible?.map(row =>
      <li key={row.id}><Panel><div className="platform-category-card">
        <div className="platform-context"><span className="platform-category-icon"><Layers3 size={21} aria-hidden="true" /></span><Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'Activa' : 'Inactiva'}</Badge></div>
        <h2>{row.name}</h2>
        <p>Creada <DateText value={row.createdAt} /></p>
      </div></Panel></li>)}</ul>}
  </section>
}

export function PlatformAccount({ expiresAt }: { expiresAt?: string }) {
  return <Panel><div className="operation-body platform-profile">
    <span className="platform-category-icon"><ShieldCheck size={22} aria-hidden="true" /></span>
    <div><h2>Cuenta administrativa</h2><p className="platform-note">Sesión de Google · Superadministrador</p></div>
    <dl className="summary-grid">
      <div><dt>Ámbito</dt><dd>Boticas, licencias y catálogo global</dd></div>
      {expiresAt && <div><dt>Vencimiento de sesión</dt><dd><DateText value={expiresAt} /></dd></div>}
    </dl>
    <p>El acceso comercial requiere una membresía en cada botica. La autoridad de plataforma se administra mediante configuración privada.</p>
    <div className="platform-account-actions"><Link to="/seleccionar-botica">Ir a mis boticas</Link><LogoutButton /></div>
  </div></Panel>
}
