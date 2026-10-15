// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const css = readFileSync(new URL('../src/shared/styles/tokens.css', import.meta.url), 'utf8')
const tokens = Object.fromEntries([...css.matchAll(/--([\w-]+):\s*(#[a-f\d]{6});/gi)].map(match => [match[1], match[2]]))
function luminance(name: string) {
  const hex = tokens[name]
  if (!hex) throw new Error(`Missing opaque color token: ${name}`)
  const channels = [1, 3, 5].map(offset => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722
}
function contrast(foreground: string, background: string) {
  const a = luminance(foreground), b = luminance(background)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

describe('WCAG 2.2 semantic color pairs', () => {
  const pairs = [
    ...['surface', 'canvas', 'surface-muted', 'primary-soft'].flatMap(bg => ['text', 'text-muted', 'primary', 'info'].map(fg => [fg, bg])),
    ['on-primary', 'primary'], ['on-primary', 'primary-hover'],
    ['success', 'success-soft'], ['warning', 'warning-soft'], ['error', 'error-soft'], ['info', 'info-soft'],
  ]
  it.each(pairs)('%s on %s meets normal text 4.5:1 without rounding up', (fg, bg) => {
    expect(contrast(fg!, bg!)).toBeGreaterThanOrEqual(4.5)
  })
  it.each(['surface', 'canvas', 'surface-muted', 'primary-soft'])('focus and control outlines remain visible on %s', bg => {
    expect(contrast('focus', bg)).toBeGreaterThanOrEqual(3)
    expect(contrast('control-border', bg)).toBeGreaterThanOrEqual(3)
  })
})
