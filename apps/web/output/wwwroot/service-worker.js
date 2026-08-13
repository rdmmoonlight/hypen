const CACHE_NAME = 'hypen-vault-v1';
const FILES_TO_CACHE = [
  './',
  './index.html',
  './css/app.css',
  './manifest.json',
  './icon-192.png',
  './icon-512.png',
  './_framework/blazor.webassembly.js'
];

// 1. Install & Cache Asset Dasar
self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(FILES_TO_CACHE);
    })
  );
  self.skipWaiting();
});

// 2. Activate & Hapus Cache Lama
self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keyList) => {
      return Promise.all(
        keyList.map((key) => {
          if (key !== CACHE_NAME) {
            return caches.delete(key);
          }
        })
      );
    })
  );
  self.clients.claim();
});

// 3. Fetch Strategy: Cache First, Fallback to Network
self.addEventListener('fetch', (event) => {
  // Abaikan request ke API Backend/External CDN agar selalu fresh
  if (event.request.url.includes('/api/') || event.request.url.includes('last.fm')) {
    return;
  }

  event.respondWith(
    caches.match(event.request).then((response) => {
      return response || fetch(event.request);
    })
  );
});
