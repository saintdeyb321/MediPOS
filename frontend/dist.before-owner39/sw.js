const CACHE = "medipos-shell-7e50a88730ca277636a0";
const ASSETS = ["/assets/index-BfMs2VLj.js","/assets/index-OvVB6atY.css","/index.html","/favicon.svg","/manifest.webmanifest"];
self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS))));
// No skipWaiting: a new shell must not replace an active checkout/outbox session.
self.addEventListener('activate', event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith('medipos-shell-') && key !== CACHE).map(key => caches.delete(key))))));
self.addEventListener('fetch', event => {
  const request = event.request; const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin || /^\/(api|auth|platform|health|openapi)(\/|$)/i.test(url.pathname)) return;
  if (request.mode === 'navigate') {
    event.respondWith(fetch(request).catch(async () => (await caches.open(CACHE)).match('/index.html').then(page => page || Response.error())));
    return;
  }
  if (!url.search && ASSETS.includes(url.pathname)) event.respondWith(caches.open(CACHE).then(async cache => (await cache.match(url.pathname)) || fetch(request)));
});