// Minimal service worker covering all three roles' task/records pages. Its
// only job is to let the last-loaded page still render (read-only, from
// cache) when the office's internet drops, instead of the browser showing
// its own "no internet" error page. It does NOT do background sync itself —
// offline-sync.js handles queuing and replay once the page is open and the
// browser fires the 'online' event.
var CACHE_NAME = 'dti-onelink-shell-v3';
var CACHE_URLS = [
    '/Employee/TaskManagement',
    '/Dashboard/AdminDashboard',
    '/Dashboard/SuperAdminDashboard',
    '/Records',
    '/css/adminlayout.css',
    '/js/adminlayout.js',
    '/js/offline-sync.js',
    '/js/employeetaskdetails.js',
    '/js/records.js'
];

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(CACHE_NAME).then(function (cache) {
            // Best-effort: a failed individual URL shouldn't block install.
            return Promise.all(
                CACHE_URLS.map(function (url) {
                    return cache.add(url).catch(function () { });
                })
            );
        })
    );
    self.skipWaiting();
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys().then(function (names) {
            return Promise.all(
                names
                    .filter(function (name) { return name !== CACHE_NAME; })
                    .map(function (name) { return caches.delete(name); })
            );
        })
    );
    self.clients.claim();
});

// Network-first for page navigations and the listed static assets, so the
// live site is always preferred when reachable; falls back to whatever was
// last cached for that exact URL only when the network request actually
// fails. POSTs and other non-GET requests are never intercepted — writes
// always go straight to the network (or get queued by offline-sync.js at
// the page level), never served from this cache.
self.addEventListener('fetch', function (event) {
    if (event.request.method !== 'GET') {
        return;
    }

    var url = new URL(event.request.url);
    var isNavigation = event.request.mode === 'navigate';
    var isCachedAsset = CACHE_URLS.indexOf(url.pathname) !== -1;

    if (!isNavigation && !isCachedAsset) {
        return; // let the browser handle it normally
    }

    // Cache.put() throws a TypeError if given a Request whose mode is
    // "navigate" (which event.request always is for a page navigation) — so
    // the cache key here is the URL STRING, never event.request directly.
    // Getting this wrong is what broke every page load in production: the
    // throw inside this .then() rejected the respondWith() promise, which
    // is exactly what produces net::ERR_FAILED on an otherwise-successful
    // navigation.
    var cacheKey = url.pathname;

    event.respondWith(
        fetch(event.request)
            .then(function (response) {
                var copy = response.clone();
                caches.open(CACHE_NAME).then(function (cache) {
                    cache.put(cacheKey, copy).catch(function () { /* best-effort cache; never fail the real navigation for it */ });
                });
                return response;
            })
            .catch(function () {
                return caches.match(cacheKey).then(function (cached) {
                    if (cached) {
                        return cached;
                    }
                    // No cached copy of this exact page — fall back to
                    // whichever role's shell page was cached, since it's
                    // better than the browser's own offline error page.
                    return caches.match('/Employee/TaskManagement')
                        .then(function (fallback) { return fallback || caches.match('/Records'); });
                });
            })
    );
});
