const CACHE = "medipos-shell-ce08f7e326179c231f9f";
const ASSETS = ["/assets/index-B9nTitzI.css","/assets/index-BIUMvklv.js","/index.html","/favicon.svg","/manifest.webmanifest"];
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