import { ApiError } from '../../shared/api/http'
import { oneOf, text } from '../../shared/api/contracts'

// TenantHttp: JsonStringEnumConverter(SnakeCaseLower, allowIntegerValues: false).
export const days = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'] as const
export type Weekday = typeof days[number]
export const weekday = oneOf(...days)
export const dayLabels: Record<Weekday, string> = { sunday: 'Domingo', monday: 'Lunes', tuesday: 'Martes', wednesday: 'Miércoles', thursday: 'Jueves', friday: 'Viernes', saturday: 'Sábado' }
export interface WorkWindow { dayOfWeek: Weekday; startTime: string; endTime: string }

// PostgreSQL time and WorkSchedule permit microseconds. A seventh zero emitted by TimeOnly is lossless.
export function timeInput(value: string): string | null {
  const match = /^([01]\d|2[0-3]):([0-5]\d)(?::([0-5]\d)(?:\.(\d{1,7}))?)?$/.exec(value.trim())
  if (!match || match[4]?.length === 7 && !match[4].endsWith('0')) return null
  const fraction = (match[4] ?? '').replace(/0+$/, '')
  return `${match[1]}:${match[2]}:${match[3] ?? '00'}${fraction ? '.' + fraction : ''}`
}
export function wireTime(value: unknown): string {
  const input = text(value)
  if (!/^\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?$/.test(input)) throw new ApiError(0, 'client.unexpected_response')
  return timeInput(input) ?? (() => { throw new ApiError(0, 'client.unexpected_response') })()
}
function timeKey(value: string) { const [seconds, fraction = ''] = value.split('.'); return seconds! + '.' + fraction.padEnd(6, '0') }
export function validSchedule(windows: readonly WorkWindow[]): boolean {
  const keys = new Set<string>()
  for (const window of windows) {
    if (!days.includes(window.dayOfWeek)) return false
    const start = timeInput(window.startTime); const end = timeInput(window.endTime)
    if (!start || !end || timeKey(start) >= timeKey(end)) return false
    const key = window.dayOfWeek + '/' + timeKey(start) + '/' + timeKey(end)
    if (keys.has(key)) return false
    keys.add(key)
  }
  return true // Empty is representable; it does not grant permanent operational access.
}
export const windowLabel = (window: WorkWindow) => `${dayLabels[window.dayOfWeek]} ${window.startTime}–${window.endTime}`
