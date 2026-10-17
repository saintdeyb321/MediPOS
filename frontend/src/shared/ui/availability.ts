export type Availability = 'implemented' | 'partial' | 'not_started'
export const availabilityLabels: Record<Availability, string> = {
  implemented: 'Implementado',
  partial: 'Parcial',
  not_started: 'No iniciado',
}
