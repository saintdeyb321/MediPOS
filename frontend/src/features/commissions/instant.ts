import { dateOnly } from '../../shared/api/contracts'

// DateTimeOffset supports seven fractional digits. Compare ticks, never truncate to JS milliseconds.
export function utcTicks(value: string): bigint | null {
  const match = /^(\d{4}-\d{2}-\d{2})T([01]\d|2[0-3]):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)
  if (!match) return null
  try { dateOnly(match[1]); const milliseconds = Date.parse(`${match[1]}T${match[2]}:${match[3]}:${match[4]}Z`); return BigInt(milliseconds) * 10000n + BigInt((match[5] ?? '').padEnd(7, '0')) } catch { return null }
}
