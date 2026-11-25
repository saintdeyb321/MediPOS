import { useEffect, useRef, useState } from 'react'
import { Link, Outlet, useSearchParams } from 'react-router'
import { Building2, CircleUserRound, Home, KeyRound, Layers3, Menu, ShieldCheck, UserRoundCheck } from 'lucide-react'
import { ApiError } from '../../shared/api/http'
import { useQueryClient } from '@tanstack/react-query'
import { PlatformAdmissionError, usePlatformAdmission } from '../../features/platform'
import { platformDestination } from '../../features/platform/presentation'
import { LogoutButton } from '../../features/auth'
import { Brand } from '../../shared/ui/Brand'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { LoadingState } from '../../shared/ui/States'
import './platform.css'

const areas = [ ['home', 'Inicio de plataforma', Home], ['businesses', 'Boticas', Building2], ['owners', 'Propietarios pendientes', UserRoundCheck],
  ['licenses', 'Licencias', KeyRound], ['catalog', 'Catálogo global', Layers3], ['account', 'Mi cuenta', CircleUserRound] ] as const

export function PlatformLayout() {
  const { admission, http } = usePlatformAdmission('console')
  const client = useQueryClient(); const [params] = useSearchParams(); const content = useRef<HTMLElement>(null)
  const [menu, setMenu] = useState(false)
  const area = params.get('area') ?? 'home'
  const cached = admission.isError && Boolean(admission.data?.allowed) && admission.error instanceof ApiError && admission.error.status === 429
  const rejected = admission.isError && !cached
  useEffect(() => {
    if (rejected) { void client.cancelQueries({ queryKey: ['platform'] }); client.removeQueries({ queryKey: ['platform'] }) }
  }, [client, rejected])
  useEffect(() => () => { void client.cancelQueries({ queryKey: ['platform'] }); client.removeQueries({ queryKey: ['platform'] }) }, [client])
  useEffect(() => { content.current?.focus({ preventScroll: true }) }, [area])
  const destination = (next: string) => platformDestination(params, area, next)
  if (!http) return <Notice tone="error">Se requiere HTTPS y el backend real.</Notice>
  if (admission.isPending) return <LoadingState label="Verificando autoridad independiente de plataforma…" />
  if (rejected) return <main className="entry-page"><Brand /><PlatformAdmissionError error={admission.error} failedAt={admission.errorUpdatedAt} fetching={admission.isFetching} retry={() => void admission.refetch()} /><Link to="/seleccionar-botica">Ir a mis boticas con acceso comercial</Link><LogoutButton /></main>
  return <div className="platform-shell">
    <a href="#plataforma-contenido" className="skip-link">Saltar al contenido de plataforma</a>
    <header className="platform-mobile"><Brand /><Button variant="quiet" aria-expanded={menu} aria-controls="platform-navigation" onClick={() => setMenu(!menu)}><Menu size={20} />Menú de plataforma</Button></header>
    <aside className={`platform-sidebar${menu ? ' is-open' : ''}`}>
      <div className="platform-brand"><Brand /><span><ShieldCheck size={16} aria-hidden="true" />Administración</span></div>
      <nav id="platform-navigation" aria-label="Navegación de plataforma">{areas.map(([id, label, Icon]) => <Link key={id} to={destination(id)} replace aria-current={area === id ? 'page' : undefined} onClick={() => setMenu(false)}><Icon size={19} aria-hidden="true" />{label}</Link>)}</nav>
      <div className="platform-account"><span>Cuenta administrativa · Google</span>{area !== 'account' && <LogoutButton />}</div>
    </aside>
    <main id="plataforma-contenido" className="platform-main" ref={content} tabIndex={-1}>{cached && <PlatformAdmissionError cached error={admission.error} failedAt={admission.errorUpdatedAt} fetching={admission.isFetching} retry={() => void admission.refetch()} />}<Outlet /></main>
  </div>
}
