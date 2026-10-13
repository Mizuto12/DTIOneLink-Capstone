// Minimal service worker for the Employee task pages only. Its only job is
// to let the last-loaded Task Management / task Details page still render
// (read-only, from cache) when the office's internet drops, instead of the
// browser showing its own "no internet" error page. It does NOT do
// background sync itself — offline-sync.js handles queuing and replay once
// the page is open and the browser fires the 'online' event.
var CACHE_NAME = 'dti-onelink-employee-shell-v1';
var CACHE_URLS = [
    '/Employee/TaskManagement',
    '/css/adminlayout.css',
    '/js/adminlayout.js',
    '/js/offline-sync.js',
    '/js/employeetaskdetails.js'
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

// Network-first for navigations/pages under /Employee, so the live site is
// always preferred when reachable; falls back to the cached shell only when
// the network request actually fails. Everything else (other routes, other
// roles) is left untouched — this worker only scopes itself to what matters
// for offline task management.
self.addEventListener('fetch', function (event) {
    var url = new URL(event.request.url);
    var isEmployeeGet = event.request.method === 'GET' && url.pathname.indexOf('/Employee/') === 0;
    var isCachedAsset = CACHE_URLS.indexOf(url.pathname) !== -1;

    if (!isEmployeeGet && !isCachedAsset) {
        return; // let the browser handle it normally
    }

    event.respondWith(
        fetch(event.request)
            .then(function (response) {
                var copy = response.clone();
                caches.open(CACHE_NAME).then(function (cache) { cache.put(event.request, copy); });
                return response;
            })
            .catch(function () {
                return caches.match(event.request).then(function (cached) {
                    return cached || caches.match('/Employee/TaskManagement');
                });
            })
    );
});
