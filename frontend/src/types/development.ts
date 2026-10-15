export const scenarios = {
  owner: 'Propietario', pharmacist: 'Químico farmacéutico', cashier: 'Cajero',
  noTenants: 'Sin boticas asignadas', tenantError: 'Error al cargar boticas',
  noBranches: 'Sin sucursales asignadas', scheduleDenied: 'Fuera de horario',
  licenseDenied: 'Licencia restringida', expired: 'Sesión expirada',
} as const
export type Scenario = keyof typeof scenarios
