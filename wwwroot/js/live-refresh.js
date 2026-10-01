// ── Live updates ──────────────────────────────────────────────
// The server pushes changes through SignalR (/hubs/live, see LiveHub and
// LiveChangeBroadcaster):
//   - "notificationsChanged" → the bell refreshes (adminlayout.js listens
//     for the "live:notifications" event);
//   - "dataChanged" → on pages that opt in (<body data-live-refresh>), the
//     page reloads itself, keeping its scroll position.
// If SignalR can't connect, the page asks GET /Live/Version every few
// seconds instead, with the same result.
// A page never reloads under someone who is typing, has a dialog or menu
// open, or has changed a field; instead a small "New updates" button
// appears so they can refresh when ready.
(function () {
    var FALLBACK_CHECK_MS = 10000;
    // Disconnect once nobody has touched the page for this long, so an
    // unattended tab doesn't hold a connection (or the session) forever.
    var STOP_AFTER_IDLE_MS = 30 * 60 * 1000;
    // Don't reload within this long of the user's last click or keypress.
    var QUIET_MS = 3000;
    // A push this soon after the page's own save waits for the resync below.
    var OWN_SAVE_MS = 5000;
    var SCROLL_KEY = "live-refresh-scroll:" + location.pathname + location.search;

    var pageReloads = document.body && document.body.hasAttribute("data-live-refresh");
    var baseline = null;          // { data, notifications } the page was built from
    var pendingReload = false;
    var lastActivity = Date.now();
    var lastOwnSave = 0;
    var resyncing = Promise.resolve();
    var connection = null;        // SignalR connection, when available
    var pollTimer = null;         // fallback polling, when not
    var stopped = false;
    var banner = null;

    // ── Version check ─────────────────────────────────────────
    function fetchVersion() {
        return fetch("/Live/Version", {
            headers: { "Accept": "application/json" },
            credentials: "same-origin",
            cache: "no-store"
        }).then(function (r) {
            if (r.status === 401) { stop(); return null; } // signed out
            return r.ok ? r.json() : null;
        }).catch(function () { return null; });  // offline: try again later
    }

    // Compares the server's current version with the page's. Used on load,
    // after a reconnect (pushes may have been missed) and when polling.
    function check() {
        if (document.hidden && baseline) return;
        fetchVersion().then(function (v) {
            if (!v) return;
            if (!baseline) { baseline = v; return; }
            if (v.notifications !== baseline.notifications) {
                baseline.notifications = v.notifications;
                notificationsChanged();
            }
            dataVersionSeen(v.data);
        });
    }

    function notificationsChanged() {
        document.dispatchEvent(new CustomEvent("live:notifications"));
    }

    function dataVersionSeen(version) {
        // Right after this page saved something, wait until the baseline
        // includes that save, so the user's own change doesn't reload it.
        var wait = Date.now() - lastOwnSave < OWN_SAVE_MS ? resyncing : Promise.resolve();
        wait.then(function () {
            if (!baseline || version === baseline.data || !pageReloads) return;
            pendingReload = true;
            tryReload();
        });
    }

    // After this page saves something itself (a fetch POST, e.g. the
    // progress slider or a record), take the new version as the baseline.
    var nativeFetch = window.fetch;
    window.fetch = function (input, init) {
        var method = ((init && init.method) || (input && input.method) || "GET").toUpperCase();
        var result = nativeFetch.apply(this, arguments);
        if (method !== "GET") {
            lastOwnSave = Date.now();
            resyncing = result.then(function () {
                return fetchVersion().then(function (v) { if (v) baseline = v; });
            }, function () {});
        }
        return result;
    };

    // ── Connection ────────────────────────────────────────────
    function connect() {
        if (!window.signalR) { startPolling(); return; }

        connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/live")
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.None)
            .build();

        connection.on("dataChanged", dataVersionSeen);
        connection.on("notificationsChanged", notificationsChanged);
        connection.onreconnected(check);
        connection.onclose(function () {
            // Gave up reconnecting (e.g. server restarted for a deploy).
            if (!stopped) startPolling();
        });

        connection.start().then(stopPolling, startPolling);
    }

    function startPolling() {
        if (pollTimer || stopped) return;
        pollTimer = setInterval(check, FALLBACK_CHECK_MS);
    }

    function stopPolling() {
        clearInterval(pollTimer);
        pollTimer = null;
    }

    function stop() {
        stopped = true;
        stopPolling();
        if (connection) connection.stop();
    }

    function resume() {
        if (!stopped) return;
        stopped = false;
        if (connection && connection.state === "Disconnected") {
            connection.start().then(stopPolling, startPolling);
        } else if (!connection) {
            startPolling();
        }
        check();
    }

    // ── Is it safe to reload right now? ───────────────────────
    // Busy = the user is in the middle of something a reload would lose.
    function isBusy() {
        var active = document.activeElement;
        if (active && (active.isContentEditable ||
            /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName))) return true;

        if (document.querySelector("dialog[open], .notif-panel.open, #profileDropdown.open")) return true;

        var selection = window.getSelection && window.getSelection();
        if (selection && selection.toString().length > 0) return true;

        return hasUnsavedFields();
    }

    function recentlyActive() {
        return Date.now() - lastActivity < QUIET_MS;
    }

    // Visible fields whose value differs from what the page was built with
    // (a typed search, a half-filled form). Fields inside closed dialogs
    // or hidden panels are ignored, and so are sliders: the task progress
    // slider saves itself as it moves.
    function hasUnsavedFields() {
        var fields = document.querySelectorAll("input, textarea, select");
        for (var i = 0; i < fields.length; i++) {
            var f = fields[i];
            if (f.type === "hidden" || f.type === "range" || f.disabled || f.offsetParent === null) continue;
            if (f.type === "checkbox" || f.type === "radio") {
                if (f.checked !== f.defaultChecked) return true;
            } else if (f.tagName === "SELECT") {
                if (f.selectedIndex !== defaultSelectedIndex(f)) return true;
            } else if (f.value !== f.defaultValue) {
                return true;
            }
        }
        return false;
    }

    // With no option marked "selected", a dropdown starts on its first option.
    function defaultSelectedIndex(select) {
        for (var i = 0; i < select.options.length; i++) {
            if (select.options[i].defaultSelected) return i;
        }
        return select.options.length > 0 && !select.multiple ? 0 : -1;
    }

    function tryReload() {
        if (document.hidden) return;    // reloads when the tab is shown again
        if (isBusy()) { showBanner(); return; }
        if (recentlyActive()) return;   // the 1-second loop below retries
        reloadNow();
    }

    function reloadNow() {
        saveScroll();
        location.reload();
    }

    // ── "New updates" button, when reloading would interrupt ──
    function showBanner() {
        if (banner) return;
        banner = document.createElement("button");
        banner.type = "button";
        banner.className = "live-refresh-banner";
        banner.innerHTML = '<span class="material-symbols-outlined" aria-hidden="true">refresh</span> New updates — click to refresh';
        banner.addEventListener("click", reloadNow);
        document.body.appendChild(banner);
    }

    // ── Keep the scroll position across a reload ──────────────
    function scrollKey(el) {
        if (el.id) return "#" + el.id;
        return el.tagName + "." + (typeof el.className === "string" ? el.className.trim().split(/\s+/).join(".") : "");
    }

    function saveScroll() {
        var saved = { window: window.scrollY, elements: {} };
        var all = document.querySelectorAll("body *");
        for (var i = 0; i < all.length; i++) {
            if (all[i].scrollTop > 0) saved.elements[scrollKey(all[i])] = all[i].scrollTop;
        }
        try { sessionStorage.setItem(SCROLL_KEY, JSON.stringify(saved)); } catch (e) { /* storage blocked */ }
    }

    function restoreScroll() {
        var raw = null;
        try {
            raw = sessionStorage.getItem(SCROLL_KEY);
            sessionStorage.removeItem(SCROLL_KEY);
        } catch (e) { return; }
        if (!raw) return;
        var saved = JSON.parse(raw);
        var all = document.querySelectorAll("body *");
        for (var i = 0; i < all.length; i++) {
            var top = saved.elements[scrollKey(all[i])];
            if (top) all[i].scrollTop = top;
        }
        window.scrollTo(0, saved.window || 0);
    }

    // ── Activity ──────────────────────────────────────────────
    function noteActivity() {
        lastActivity = Date.now();
        resume();
    }

    ["pointerdown", "keydown", "wheel", "touchstart"].forEach(function (type) {
        document.addEventListener(type, noteActivity, { passive: true, capture: true });
    });

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) { noteActivity(); check(); }
    });

    setInterval(function () {
        if (!stopped && Date.now() - lastActivity > STOP_AFTER_IDLE_MS) stop();
        // A reload that was waiting runs as soon as the user is done.
        if (pendingReload && !document.hidden && !isBusy() && !recentlyActive()) reloadNow();
    }, 1000);

    document.addEventListener("DOMContentLoaded", restoreScroll);
    if (document.readyState !== "loading") restoreScroll();

    check();     // baseline right away, so changes made after the page was built are caught
    connect();
})();
