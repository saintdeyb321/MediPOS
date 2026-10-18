import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService } from '../access'
import { HttpClient } from '../../shared/api/http'
import { TenantOnboardingPage } from './TenantOnboardingPage'
import { id, managementServer, workspace, json } from '../notifications/management.test.support'

// UI/HTTP contract regression with synthetic transport; no real Google/PostgreSQL acceptance is claimed.
function mount(role: 'owner' | 'cashier' = 'owner', pending = '', allowed = true) {
  const base = managementServer(); base.management.legal = []; base.management.branches = []
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    const p = String(path)
    if (p === '/auth/me') return json({ userId: workspace.session.userId })
    if (p.startsWith('/api/me/tenants')) return json({ items: [{ tenantId: workspace.tenantId, tradingName: 'Synthetic branchless botica', role }], nextAfterTenantId: null })
    if (p.endsWith('/access')) return json({ isAllowed: allowed && role === 'owner', code: allowed ? 'access.allowed' : 'access.license_denied', context: { ...workspace.access, role, branchId: null } })
    return base.fetcher(path, init)
  })
  const http = new HttpClient(fetcher)
  render(<RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={[`/boticas/${workspace.tenantId}/configuracion` + pending]}><Routes><Route path="/boticas/:tenantId/configuracion" element={<TenantOnboardingPage />} /></Routes></MemoryRouter></RuntimeProvider>)
  return { ...base, fetcher }
}
it('allows an Owner without branches to read license and create the first entity and branch without a fake branch ID', async () => {
  const fake = mount(); await screen.findByText('trial')
  expect(await screen.findByRole('heading', { name: /Synthetic branchless botica/ })).toBeInTheDocument()
  expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  fireEvent.change(screen.getByRole('textbox', { name: 'Razón social' }), { target: { value: 'Synthetic QA entity' } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Identificador de la entidad' }), { target: { value: 'QA-NO-FISCAL' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo la creación persistente/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Crear entidad legal' })); fireEvent.click(screen.getByRole('button', { name: 'Crear entidad legal' }))
  await screen.findByText(/Registro reconsultado: Synthetic QA entity/)
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué inequívocamente/ })); fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre de la nueva sucursal' }), { target: { value: 'Synthetic QA first branch' } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Entidad legal existente' }), { target: { value: id(540) } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo crear Synthetic QA first branch/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Crear sucursal' }))
  await screen.findByText(/Registro reconsultado: Synthetic QA first branch/)
  expect(screen.getByRole('link', { name: /Elegir sucursal y entrar/ })).toHaveAttribute('href', `/boticas/${workspace.tenantId}/sucursales`)
  expect(fake.writes()).toHaveLength(2)
  expect(fake.writes().map(([path]) => String(path))).toEqual([`/api/tenants/${workspace.tenantId}/legal-entities`, `/api/tenants/${workspace.tenantId}/branches`])
})
it.each(['cashier', 'restricted-license'] as const)('denies %s before any private directory read or creation', async scenario => {
  const fake = mount(scenario === 'cashier' ? 'cashier' : 'owner', '', scenario !== 'restricted-license')
  await screen.findByText(/La configuración requiere una membresía Owner activa/)
  expect(fake.fetcher.mock.calls.some(([path]) => /\/license$|\/legal-entities|\/branches/.test(String(path)))).toBe(false)
  expect(fake.writes()).toHaveLength(0)
})
it('preserves an uncertain first-entity intent across reload without automatically sending another POST', async () => {
  const fake = mount('owner', '?settingsPending=legal')
  await screen.findByRole('heading', { name: 'Alta pendiente de conciliación' })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Crear entidad legal' })).toBeDisabled())
  expect(fake.writes()).toHaveLength(0)
})
