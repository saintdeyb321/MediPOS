import { focusManager } from '@tanstack/react-query'
import { act } from '@testing-library/react'
import { vi } from 'vitest'

export async function refocus(ageMs = 0) {
  const now = Date.now()
  const clock = ageMs > 0 ? vi.spyOn(Date, 'now').mockImplementation(() => now + ageMs) : null
  try {
    await act(async () => {
      focusManager.setFocused(false)
      focusManager.setFocused(true)
    })
  } finally {
    clock?.mockRestore()
  }
}
