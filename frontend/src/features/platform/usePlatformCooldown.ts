import { useEffect, useState } from 'react'
import { ApiError } from '../../shared/api/http'

export function cooldownDeadline(error: unknown, failedAt: number) {
  if (!(error instanceof ApiError) || error.status !== 429) return 0
  return failedAt + (error.retryAfterSeconds ?? (error.code === 'platform.busy' ? 2 : 60)) * 1000
}

export function usePlatformCooldown(deadline: number, failedAt: number) {
  const [lastTick, setLastTick] = useState(() => Date.now())
  useEffect(() => {
    if (!deadline) return
    const timer = window.setInterval(() => {
      const now = Date.now()
      setLastTick(now)
      if (now >= deadline) window.clearInterval(timer)
    }, 1000)
    return () => window.clearInterval(timer)
  }, [deadline])
  return Math.max(0, Math.ceil((deadline - Math.max(lastTick, failedAt)) / 1000))
}
