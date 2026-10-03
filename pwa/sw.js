const CACHE = "kubgau-pwa-2";
const FILES = [
  "./",
  "./index.html",
  "./app.js",
  "./protocol.js",
  "./qrcode.js",
  "./jsQR.js",
  "./pako.min.js",
  "./logo.png",
  "./manifest.webmanifest",
  "./fonts/Manrope-Regular.ttf",
  "./fonts/Manrope-Medium.ttf",
  "./fonts/Manrope-SemiBold.ttf",
  "./fonts/Manrope-Bold.ttf",
  "./fonts/Manrope-ExtraBold.ttf"
];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(FILES)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  event.respondWith(caches.match(event.request).then((cached) => cached || fetch(event.request)));
});
