import { useEffect, useRef } from 'react'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Link, NavLink, Outlet, useLocation, useNavigate, useParams } from 'react-router'
import { ArrowRightLeft, ChevronDown, MapPin, Menu, ShieldCheck, Store, UserRound, X } from 'lucide-react'
import { WorkspaceContext } from '../app/contexts'
import type { Workspace } from '../app/contexts'
import { availableNavigation } from '../app/navigation'
import { useRuntime, useSession } from '../hooks/useSession'
import { BackLink, Brand, ErrorState, Loading } from '../components/ui'
import { SelectionLayout } from '../features/workspace/selection'
import { isUuid, roleLabels } from '../types/contracts'
import { accessMessage } from '../services/errors'
import { LogoutButton } from '../features/auth/LogoutButton'

export function WorkspaceLayout() {
  const { tenantId = '', branchId = '' } = useParams(); const { service } = useRuntime()
  const { pathname } = useLocation(); const session = useSession()
  const valid = isUuid(tenantId) && isUuid(branchId)
  const tenants = useInfiniteQuery({ queryKey: ['tenants'], initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => service.tenants(pageParam, signal), getNextPageParam: page => page.nextAfterTenantId ?? undefined,
  })
  const branches = useQuery({ queryKey: ['tenant', tenantId, 'branches'], queryFn: ({ signal }) => service.branches(tenantId, signal), enabled: valid, staleTime: 0 })
  // Recheck on every route, remount, focus and at least once per minute. This is presentation only;
  // every later business request still requires the backend's own authorization.
  const access = useQuery({ queryKey: ['tenant', tenantId, branchId, 'access', pathname], queryFn: ({ signal }) => service.access(tenantId, branchId, signal), enabled: valid, staleTime: 0, refetchOnMount: 'always', refetchInterval: 60_000 })
  const back = <BackLink to={`/boticas/${tenantId}/sucursales`}>Elegir otra sucursal</BackLink>
  if (!valid) return <SelectionLayout><BackLink to="/seleccionar-botica">Elegir botica</BackLink><div className="state-block"><h1>Dirección no válida</h1><p>Selecciona una botica y sucursal para continuar.</p></div></SelectionLayout>
  if (access.isPending || branches.isPending || session.isPending || access.isFetching) return <Loading label="Verificando acceso a la sucursal…" />
  if (access.isError || branches.isError || session.isError) return <SelectionLayout>{back}<ErrorState error={access.error ?? branches.error ?? session.error} retry={() => { void access.refetch(); void branches.refetch(); void session.refetch() }} /></SelectionLayout>
  const branch = branches.data?.find(b => b.id === branchId)
  const context = access.data.context
  if (!access.data.isAllowed || !context || !branch || context.userId !== session.data?.userId || context.tenantId !== tenantId || context.branchId !== branchId)
    return <SelectionLayout>{back}<div className="panel state-block" role="alert"><span className="state-icon warning"><ShieldCheck /></span><h1>Tu acceso está restringido</h1><p>{accessMessage(access.data.isAllowed ? 'access.context_changed' : access.data.code)}</p><button className="button secondary" onClick={() => void access.refetch()}>Comprobar acceso de nuevo</button></div></SelectionLayout>
  const tenant = tenants.data?.pages.flatMap(page => page.items).find(t => t.tenantId === tenantId)
  const workspace: Workspace = { tenantId, branchId, tenantName: tenant?.tradingName ?? 'Botica seleccionada', branch, access: context, session: session.data!, base: `/boticas/${tenantId}/sucursales/${branchId}` }
  return <WorkspaceContext.Provider value={workspace}><WorkspaceShell workspace={workspace} /></WorkspaceContext.Provider>
}
function WorkspaceShell({ workspace: w }: { workspace: Workspace }) {
  const { preview } = useRuntime()
  const mobile = useRef<HTMLDialogElement>(null); const navigate = useNavigate(); const { pathname } = useLocation()
  const links = availableNavigation(w.access.role)
  const page = links.find(item => (item.id ? `${w.base}/${item.id}` : w.base) === pathname)
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      const target = event.target
      if (event.altKey && event.key.toLowerCase() === 'v' && !(target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)))) { event.preventDefault(); navigate(`${w.base}/ventas`) }
    }
    window.addEventListener('keydown', shortcut)
    return () => window.removeEventListener('keydown', shortcut)
  }, [navigate, w.base])
  function menu() {
    return <><div className="sidebar-brand"><Link to={w.base} aria-label="MediPOS, inicio"><Brand /></Link></div><div className="sidebar-tenant"><span className="tenant-symbol"><Store size={18} /></span><div><strong>{w.tenantName}</strong><span>Espacio de trabajo</span></div></div>
      <nav aria-label="Navegación principal">{(['operation', 'management'] as const).map(section => {
        const items = links.filter(item => item.section === section)
        return items.length > 0 && <div className="nav-section" key={section}><p>{section === 'operation' ? 'TU BOTICA' : 'GESTIÓN'}</p>{items.map(item => <NavLink key={item.id} to={item.id ? `${w.base}/${item.id}` : w.base} end className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`} onClick={() => { if (mobile.current?.open) mobile.current.close() }}><item.icon size={19} /><span>{item.label}</span>{item.id === 'ventas' && <kbd>Alt V</kbd>}</NavLink>)}</div>
      })}</nav><div className="sidebar-bottom"><span className="sidebar-status"><ShieldCheck size={16} /> {preview ? 'Acceso de ejemplo' : 'Acceso verificado'}</span><p>{preview ? 'Vista de desarrollo, sin sesión real.' : 'Conectado a tu espacio de trabajo.'}</p><Link to="/seleccionar-botica" className="switch-tenant"><ArrowRightLeft size={16} /> Cambiar botica</Link></div></>
  }
  return <div className="app-shell"><a href="#contenido" className="skip-link">Saltar al contenido</a><aside className="desktop-sidebar">{menu()}</aside>
    <dialog ref={mobile} className="mobile-navigation" onClick={e => { if (e.target === e.currentTarget) mobile.current?.close() }}><button className="icon-button close-nav" aria-label="Cerrar navegación" onClick={() => mobile.current?.close()}><X /></button>{menu()}</dialog>
    <div className="workspace"><header className="workspace-header"><div className="header-location"><button className="icon-button mobile-menu-toggle" aria-label="Abrir navegación" onClick={() => mobile.current?.showModal()}><Menu /></button><span className="header-page">{page?.label ?? 'Tu botica'}</span><span className="header-divider" /><Link className="branch-switch" to={`/boticas/${w.tenantId}/sucursales`}><MapPin size={16} /><span>{w.branch.name}</span><ChevronDown size={14} /></Link></div><details className="account-menu"><summary aria-label="Mi cuenta"><span className="user-avatar"><UserRound size={19} /></span><span className="account-label">{roleLabels[w.access.role]}<small>Mi cuenta</small></span><ChevronDown size={14} /></summary><div className="account-dropdown"><strong>Mi cuenta</strong><p>Identificador de usuario</p><code>{w.session.userId}</code><LogoutButton /></div></details></header>
      <main id="contenido" tabIndex={-1} className="workspace-main"><Outlet /></main><footer className="workspace-footer"><span><span className="status-dot" /> {w.branch.name}</span><span>MediPOS · Tu botica, en orden</span></footer>
    </div>
  </div>
}
