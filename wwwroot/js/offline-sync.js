// ==========================================================================
// Offline task sync (Employee role only).
//
// Scope: queues two kinds of actions locally (IndexedDB) when the network is
// unreachable — progress/status updates and proof-of-completion submissions
// — and replays them against the SAME endpoints the live UI already uses
// (/Employee/UpdateProgress, /Employee/SubmitProof) once the connection
// returns. No new server endpoints or DB columns: the existing workflow
// guards in EmployeeController (e.g. "already completed", "awaiting review")
// are what reject a stale queued action if the task changed while offline —
// this module just surfaces that rejection instead of retrying forever.
//
// Exposed globally as window.DTIOffline for other page scripts to call:
//   DTIOffline.queueProgress({ taskId, progress, requestedStatus })
//   DTIOffline.queueProof({ taskId, remarks, file })
//   DTIOffline.trySync()
// ==========================================================================
(function () {
    var DB_NAME = 'dti-onelink-offline';
    var DB_VERSION = 1;
    var STORE_PROGRESS = 'progressQueue';
    var STORE_PROOF = 'proofQueue';

    var dbPromise = null;
    var syncing = false;

    function openDb() {
        if (dbPromise) {
            return dbPromise;
        }
        dbPromise = new Promise(function (resolve, reject) {
            if (!('indexedDB' in window)) {
                reject(new Error('IndexedDB not supported'));
                return;
            }
            var req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onupgradeneeded = function () {
                var db = req.result;
                if (!db.objectStoreNames.contains(STORE_PROGRESS)) {
                    db.createObjectStore(STORE_PROGRESS, { keyPath: 'id', autoIncrement: true });
                }
                if (!db.objectStoreNames.contains(STORE_PROOF)) {
                    db.createObjectStore(STORE_PROOF, { keyPath: 'id', autoIncrement: true });
                }
            };
            req.onsuccess = function () { resolve(req.result); };
            req.onerror = function () { reject(req.error); };
        });
        return dbPromise;
    }

    function withStore(storeName, mode, fn) {
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction(storeName, mode);
                var store = tx.objectStore(storeName);
                var result = fn(store);
                tx.oncomplete = function () { resolve(result); };
                tx.onerror = function () { reject(tx.error); };
            });
        });
    }

    function addRecord(storeName, record) {
        return withStore(storeName, 'readwrite', function (store) {
            store.add(record);
        });
    }

    function getAll(storeName) {
        return openDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction(storeName, 'readonly');
                var store = tx.objectStore(storeName);
                var req = store.getAll();
                req.onsuccess = function () { resolve(req.result || []); };
                req.onerror = function () { reject(req.error); };
            });
        });
    }

    function deleteRecord(storeName, id) {
        return withStore(storeName, 'readwrite', function (store) {
            store.delete(id);
        });
    }

    function countAll() {
        return Promise.all([getAll(STORE_PROGRESS), getAll(STORE_PROOF)]).then(function (results) {
            return results[0].length + results[1].length;
        });
    }

    function getCsrfToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    // ── Public: queue an action ───────────────────────────────────────────

    function queueProgress(entry) {
        return addRecord(STORE_PROGRESS, {
            taskId: entry.taskId,
            progress: entry.progress,
            requestedStatus: entry.requestedStatus || null,
            queuedAt: new Date().toISOString()
        }).then(updatePill);
    }

    function queueProof(entry) {
        return addRecord(STORE_PROOF, {
            taskId: entry.taskId,
            remarks: entry.remarks,
            fileBlob: entry.file,
            fileName: entry.file ? entry.file.name : null,
            queuedAt: new Date().toISOString()
        }).then(updatePill);
    }

    // ── Sync ────────────────────────────────────────────────────────────

    function replayProgress(record) {
        var body = new URLSearchParams();
        body.append('id', record.taskId);
        body.append('progress', String(record.progress));
        body.append('__RequestVerificationToken', getCsrfToken());

        return fetch('/Employee/UpdateProgress', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        }).then(function (res) { return classifySyncResponse(res); });
    }

    function replayProof(record) {
        var form = new FormData();
        form.append('Id', record.taskId);
        form.append('Remarks', record.remarks || '');
        if (record.fileBlob) {
            form.append('ProofFile', record.fileBlob, record.fileName || 'proof');
        }
        form.append('__RequestVerificationToken', getCsrfToken());

        return fetch('/Employee/SubmitProof', {
            method: 'POST',
            body: form
        }).then(function (res) { return classifySyncResponse(res); });
    }

    // Three outcomes: 'ok' (applied), 'rejected' (the task changed while
    // offline — the controller's own workflow guard said no), or
    // 'session-expired' ([RequireLogin] redirected to the login page
    // instead of answering the request at all).
    function classifySyncResponse(res) {
        if (res.redirected || res.url.indexOf('/Account/Login') !== -1) {
            return { outcome: 'session-expired' };
        }
        if (res.ok) {
            return { outcome: 'ok' };
        }
        return res.json().catch(function () { return null; }).then(function (data) {
            return { outcome: 'rejected', message: data && data.message ? data.message : 'This task changed while you were offline.' };
        });
    }

    function trySync() {
        if (syncing || !navigator.onLine) {
            return Promise.resolve();
        }
        syncing = true;
        setStatus('syncing');

        return Promise.all([getAll(STORE_PROGRESS), getAll(STORE_PROOF)])
            .then(function (results) {
                var progressItems = results[0].sort(function (a, b) { return a.id - b.id; });
                var proofItems = results[1].sort(function (a, b) { return a.id - b.id; });
                var chain = Promise.resolve();
                var stoppedForSession = false;
                var conflicts = [];

                progressItems.concat(proofItems).forEach(function (record) {
                    var isProof = proofItems.indexOf(record) !== -1;
                    chain = chain.then(function () {
                        if (stoppedForSession) {
                            return;
                        }
                        var replay = isProof ? replayProof(record) : replayProgress(record);
                        return replay.then(function (result) {
                            if (result.outcome === 'ok') {
                                return deleteRecord(isProof ? STORE_PROOF : STORE_PROGRESS, record.id);
                            }
                            if (result.outcome === 'rejected') {
                                conflicts.push({ taskId: record.taskId, message: result.message });
                                // Remove it — retrying a rejected action forever
                                // would just repeat the same rejection.
                                return deleteRecord(isProof ? STORE_PROOF : STORE_PROGRESS, record.id);
                            }
                            // session-expired: leave every remaining item queued
                            // and stop, rather than losing offline work.
                            stoppedForSession = true;
                        });
                    });
                });

                return chain.then(function () {
                    return { stoppedForSession: stoppedForSession, conflicts: conflicts };
                });
            })
            .then(function (outcome) {
                syncing = false;
                return countAll().then(function (remaining) {
                    if (outcome.stoppedForSession) {
                        setStatus('session-expired', remaining);
                    } else if (outcome.conflicts.length > 0) {
                        setStatus('conflicts', remaining, outcome.conflicts);
                    } else if (remaining > 0) {
                        setStatus('offline', remaining);
                    } else {
                        setStatus('online');
                    }
                });
            })
            .catch(function () {
                syncing = false;
                updatePill();
            });
    }

    // ── Status pill ────────────────────────────────────────────────────

    function setStatus(kind, queuedCount, conflicts) {
        var pill = document.getElementById('offlineStatusPill');
        if (!pill) {
            return;
        }
        pill.classList.remove('is-online', 'is-offline', 'is-syncing', 'is-warning');
        var label;
        if (kind === 'syncing') {
            pill.classList.add('is-syncing');
            label = 'Syncing…';
        } else if (kind === 'offline') {
            pill.classList.add('is-offline');
            label = 'Offline — ' + queuedCount + ' change' + (queuedCount === 1 ? '' : 's') + ' queued';
        } else if (kind === 'session-expired') {
            pill.classList.add('is-warning');
            label = 'Sign in again to sync ' + queuedCount + ' queued change' + (queuedCount === 1 ? '' : 's');
        } else if (kind === 'conflicts') {
            pill.classList.add('is-warning');
            label = conflicts.length + ' change' + (conflicts.length === 1 ? '' : 's') + " couldn't sync — review task" + (conflicts.length === 1 ? '' : 's');
            if (conflicts.length > 0) {
                console.warn('DTIOffline: sync conflicts', conflicts);
            }
        } else {
            pill.classList.add('is-online');
            label = 'Online';
        }
        pill.textContent = label;
        pill.hidden = kind === 'online' && (!queuedCount || queuedCount === 0);
    }

    function updatePill() {
        countAll().then(function (n) {
            if (!navigator.onLine) {
                setStatus('offline', n);
            } else if (n > 0) {
                setStatus('offline', n); // queued but not yet synced
            } else {
                setStatus('online');
            }
        }).catch(function () { /* IndexedDB unavailable — no pill, fail quiet */ });
    }

    // ── Wire up ────────────────────────────────────────────────────────

    window.addEventListener('online', trySync);
    window.addEventListener('offline', updatePill);
    document.addEventListener('DOMContentLoaded', function () {
        updatePill();
        if (navigator.onLine) {
            trySync();
        }
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/sw.js').catch(function () {
                // Offline page-shell caching is a nicety, not a requirement —
                // fail quiet if it can't register (e.g. unsupported browser).
            });
        }
    });

    window.DTIOffline = {
        queueProgress: queueProgress,
        queueProof: queueProof,
        trySync: trySync
    };
})();
