// @vitest-environment node
import { expect, it } from 'vitest'
import { JsonNumber, stringifyExactJson } from './exactJson'
import { decimalLabel, multiply } from './decimal'

it.each([
  ['20.000000000000', 0, '20'], ['1.0000', 2, '1.00'], ['10.0000000000000000', 2, '10.00'],
  ['1.2345', 2, '1.2345'], ['0.00010000', 2, '0.0001'], ['-0.1200', 2, '-0.12'],
  ['99999999999999.9999', 2, '99999999999999.9999'], ['1e2', 2, '100.00'],
])('formats %s without insignificant zeroes or losing significant precision', (literal, minimum, expected) => {
  const exact = new JsonNumber(literal)
  expect(decimalLabel(exact, minimum)).toBe(expected)
  expect(stringifyExactJson(exact)).toBe(literal)
})
it('shows a box of ten at an exact base price of one sol without changing its numeric literal', () => {
  const price = multiply(new JsonNumber('1.0000'), new JsonNumber('10.000000000000'))
  expect(decimalLabel(price, 2)).toBe('10.00')
  expect(price.literal).toBe('10.0000000000000000')
})
