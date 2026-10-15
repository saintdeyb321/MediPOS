// @vitest-environment node
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'
import ts from 'typescript'

const root = fileURLToPath(new URL('../src/', import.meta.url))
function files(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => entry.isDirectory() ? files(resolve(directory, entry.name)) : [resolve(directory, entry.name)])
}
it('keeps shared independent, features below app, and cross-feature imports public', () => {
  const violations: string[] = []
  for (const file of files(root).filter(path => /\.tsx?$/.test(path) && !path.includes('.test.'))) {
    const local = relative(root, file).replaceAll('\\', '/')
    const tree = ts.createSourceFile(file, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true)
    for (const node of tree.statements) {
      if (!ts.isImportDeclaration(node) && !ts.isExportDeclaration(node)) continue
      const specifier = node.moduleSpecifier
      if (!specifier || !ts.isStringLiteral(specifier) || !specifier.text.startsWith('.')) continue
      const target = relative(root, resolve(dirname(file), specifier.text)).replaceAll('\\', '/')
      const fromFeature = local.match(/^features\/([^/]+)/)?.[1]
      const toFeature = target.match(/^features\/([^/]+)/)?.[1]
      if (local.startsWith('shared/') && !target.startsWith('shared/')) violations.push(`${local} → ${target}`)
      if (fromFeature && target.startsWith('app/')) violations.push(`${local} → ${target}`)
      if (fromFeature && toFeature && fromFeature !== toFeature && !/^features\/[^/]+(?:\/index)?$/.test(target)) violations.push(`${local} → ${target}`)
    }
  }
  expect(violations).toEqual([])
})

it('keeps the real application entry independent of development scenarios', () => {
  const source = readFileSync(resolve(root, 'app/App.tsx'), 'utf8')
  expect(source).not.toMatch(/development|fixtures|createPreviewService|onPreview/)
  expect(source).toContain('createMediPosService(http)')
})
