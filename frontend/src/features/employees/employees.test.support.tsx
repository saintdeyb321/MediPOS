import { render } from '@testing-library/react'
import { vi } from 'vitest'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService, WorkspaceContext } from '../access'
import type { Branch, Workspace } from '../access'
import { HttpClient } from '../../shared/api/http'
import { stringifyExactJson } from '../../shared/api/exactJson'
import type { CreateEmployeeInput, Employee, Membership } from './api'
import type { WorkWindow } from './schedule'

// Synthetic transport data only, never an OIDC/PostgreSQL identity or authorization proof.
export const id = (value: number) => `019a0162-0000-7000-8000-${String(value).padStart(12, '0')}`
export const time = '2026-10-16T12:00:00Z'
export const branch: Branch = { id: id(5), tenantId: id(2), legalEntityId: id(4), name: 'Sede unitaria', isMainHub: false, createdAt: time }
export const workspace: Workspace = { tenantId: id(2), branchId: id(5), tenantName: 'Negocio unitario', base: `/boticas/${id(2)}/sucursales/${id(5)}`, session: { userId: id(1) }, branch,
  access: { userId: id(1), tenantId: id(2), branchId: id(5), membershipId: id(3), role: 'owner', membershipIsActive: true, branchAssigned: false, withinSchedule: false, licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: time } }
export const owner: Employee = { membershipId: id(3), userId: id(1), displayName: 'Owner unitario', role: 'owner', isActive: true, createdAt: time, branchIds: [], schedule: [] }
export const worker: Employee = { membershipId: id(20), userId: id(21), displayName: 'Trabajador unitario', role: 'pharmacist', isActive: true, createdAt: time, branchIds: [id(5)], schedule: [{ dayOfWeek: 'monday', startTime: '09:00:00', endTime: '18:00:00' }] }
export const json = (data: unknown, status = 200) => new Response(stringifyExactJson(data), { status, headers: { 'Content-Type': 'application/json' } })
export const member = (employee: Employee): Membership => ({ id: employee.membershipId, tenantId: id(2), userId: employee.userId, role: employee.role, isActive: employee.isActive, createdAt: employee.createdAt, deactivatedAt: employee.isActive ? null : time })
export function employeeServer() {
  const state = { employees: [{ ...owner }, { ...worker, branchIds: [...worker.branchIds], schedule: worker.schedule.map(row => ({ ...row })) }], branches: [branch, { ...branch, id: id(6), name: 'Otra sede unitaria' }],
    loseBranches: false, loseSchedule: false, loseDeactivate: false, remainActive: false, failList: false, failBranches: false, reject: 0 }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'; const url = new URL(path, 'https://unit.test')
    if (path === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if (method === 'GET' && url.pathname.endsWith('/employee-invitations')) return json([])
    if (method === 'GET' && path.includes('/branches?')) return state.failBranches ? json({ code: 'unit.unavailable' }, 503) : json(state.branches)
    if (method === 'GET' && url.pathname.endsWith('/employees')) {
      if (state.failList) return json({ code: 'unit.unavailable' }, 503)
      const offset = Number(url.searchParams.get('offset')); const limit = Number(url.searchParams.get('limit'))
      return json(state.employees.slice(offset, offset + limit))
    }
    if (method === 'POST' || method === 'PUT') {
      if (state.reject) return json({ code: 'unit.denied' }, state.reject)
      if (url.pathname.endsWith('/employees')) {
        const value = JSON.parse(String(init?.body)) as CreateEmployeeInput
        return json({ id: id(30), tenantId: id(2), ...value, isActive: true, createdAt: time, deactivatedAt: null }, 201)
      }
      const match = /\/employees\/([^/]+)\/(branches|schedule|deactivate)$/.exec(path)
      if (match) {
        const employee = state.employees.find(row => row.membershipId === match[1])
        if (!employee) return json({ code: 'access.membership_not_found' }, 404)
        if (match[2] === 'branches') {
          employee.branchIds = (JSON.parse(String(init?.body)) as { branchIds: string[] }).branchIds
          if (state.loseBranches) throw new TypeError('Unit response loss')
          return new Response(null, { status: 204 })
        }
        if (match[2] === 'schedule') {
          employee.schedule = (JSON.parse(String(init?.body)) as { windows: WorkWindow[] }).windows
          if (state.loseSchedule) throw new TypeError('Unit response loss')
          return new Response(null, { status: 204 })
        }
        if (!state.remainActive) employee.isActive = false
        if (state.loseDeactivate) throw new TypeError('Unit response loss')
        return json(member(employee))
      }
    }
    throw new Error('Unexpected employee test route')
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
export function mountEmployees(fake: ReturnType<typeof employeeServer>, page: ReactNode, search = '', context = workspace) {
  function Location() { return <output data-testid="location">{useLocation().search}</output> }
  const http = new HttpClient(fake.fetcher)
  const tree = (current: Workspace) => <RuntimeProvider runtime={{ service: createMediPosService(http), http, preview: false }}><MemoryRouter initialEntries={['/empleados' + search]}><WorkspaceContext.Provider value={current}>{page}<Location /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>
  const view = render(tree(context)); return { ...view, changeContext: (current: Workspace) => view.rerender(tree(current)) }
}
