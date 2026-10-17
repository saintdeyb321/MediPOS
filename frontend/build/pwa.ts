import { createHash } from 'node:crypto'
import type { Plugin } from 'vite'

export function workerSource(assets: string[], version: string): string {
  return `const CACHE = ${JSON.stringify('medipos-shell-' + version)};
const ASSETS = ${JSON.stringify(assets)};
self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS))));
// No skipWaiting: a new shell must not replace an active checkout/outbox session.
self.addEventListener('activate', event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith('medipos-shell-') && key !== CACHE).map(key => caches.delete(key))))));
self.addEventListener('fetch', event => {
  const request = event.request; const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin || /^\\/(api|auth|platform|health|openapi)(\\/|$)/i.test(url.pathname)) return;
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(async () => (await caches.open(CACHE)).match('/index.html').then(page => page || Response.error())));
    return;
  }
  if (!url.search && ASSETS.includes(url.pathname)) event.respondWith(caches.open(CACHE).then(async cache => (await cache.match(url.pathname)) || fetch(request)));
});`
}
export function contingencyPwa(): Plugin {
  return { name: 'medipos-contingency-shell', apply: 'build', enforce: 'post', generateBundle(_options, bundle) {
    const names = Object.keys(bundle).filter(name => name === 'index.html' || /^assets\/[^/]+\.(js|css|woff2?)$/.test(name)).sort()
    const hash = createHash('sha256')
    for (const name of names) { const item = bundle[name]!; hash.update(name); hash.update(item.type === 'chunk' ? item.code : item.source) }
    this.emitFile({ type: 'asset', fileName: 'sw.js', source: workerSource([...names.map(name => '/' + name), '/favicon.svg', '/manifest.webmanifest'], hash.digest('hex').slice(0, 20)) })
  } }
}
