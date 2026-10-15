// @vitest-environment node
import { expect, it } from 'vitest'
import { JsonNumber, parseExactJson, stringifyExactJson } from './exactJson'
import { decimal, uint } from './contracts'

it.each(['0.1', '99999999999999.9999', '-0.00025', '1e-28', '79228162514264337593543950335'])('preserves the exact numeric token %s in both directions', literal => {
  const value = decimal(parseExactJson(literal))
  expect(value.literal).toBe(literal)
  expect(stringifyExactJson({ value })).toBe(`{"value":${literal}}`)
  expect(() => Number(value)).toThrow()
})
it('rejects lossy, nonfinite, cyclic, undefined and executable JSON input', () => {
  for (const value of [0.1, Infinity, NaN, Number.MAX_SAFE_INTEGER + 1, undefined, { omitted: undefined }, new Date(), { toJSON: () => 'hidden' }]) {
    expect(() => stringifyExactJson(value)).toThrow()
  }
  const cycle: unknown[] = []; cycle.push(cycle)
  expect(() => stringifyExactJson(cycle)).toThrow()
  for (const literal of ['NaN', '01', '1,2', '"2"', '1;DROP']) expect(() => new JsonNumber(literal)).toThrow()
})
it('distinguishes numeric tokens from strings and bounds optimistic uint versions', () => {
  expect(() => decimal('0.1')).toThrow()
  expect(uint(parseExactJson('4294967295'))).toBe(4294967295)
  for (const text of ['4294967296', '-1', '1.1', '1e3', '"1"']) expect(() => uint(parseExactJson(text))).toThrow()
})
it('preserves Unicode and numeric-looking text without interpreting it as a number', () => {
  const body = { text: 'µg / sucursal / 99999999999999.9999', value: new JsonNumber('0.5'), version: 2 }
  expect(stringifyExactJson(body)).toBe('{"text":"µg / sucursal / 99999999999999.9999","value":0.5,"version":2}')
})
