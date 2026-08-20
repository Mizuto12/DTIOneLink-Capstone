// ==========================================================
// DTI Laguna OneLink — Admin Shell JS
// (shared by both AdminLayout.cshtml and EmployeeLayout.cshtml)
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Table row click ripple ─────────────────────────
        document.querySelectorAll(".table-row-track").forEach(function (row) {
            row.addEventListener("click", function () {
                row.style.opacity = "0.5";
                setTimeout(function () {
                    row.style.opacity = "";
                }, 200);
            });
        });

        // ── Stat counter animation ─────────────────────────
        document.querySelectorAll(".stat-counter").forEach(function (el) {
            var start    = parseInt(el.dataset.start || "0", 10);
            var end      = parseInt(el.dataset.end   || "0", 10);
            var duration = 2000;
            var steps    = end - start;
            if (steps <= 0) { el.textContent = end.toLocaleString(); return; }
            var stepTime = Math.floor(duration / steps);

            var timer = setInterval(function () {
                start += 1;
                el.textContent = start.toLocaleString();
                if (start >= end) clearInterval(timer);
            }, stepTime);
        });

        // ── Notification panel ────────────────────────────
        var notifBtn    = document.getElementById("notifBtn");
        var notifPanel  = document.getElementById("notifPanel");
        var notifBadge  = document.getElementById("notifBadge");
        var notifList   = document.getElementById("notifList");
        var markAllBtn  = document.getElementById("markAllRead");

        // Starts empty — no real notifications exist yet.
        // Each notification: { id, name, avatarUrl, message, time, unread }
        var notifications = [];

        function unreadCount() {
            return notifications.filter(function (n) { return n.unread; }).length;
        }

        function escapeHtml(value) {
            var div = document.createElement("div");
            div.textContent = value == null ? "" : value;
            return div.innerHTML;
        }

        function renderNotifications() {
            if (!notifList) return;

            if (notifications.length === 0) {
                notifList.innerHTML =
                    '<div class="notif-empty">' +
                    '<span class="material-symbols-outlined">notifications_off</span>' +
                    '<p>No current notifications</p>' +
                    '</div>';
            } else {
                var html = "";
                notifications.forEach(function (n) {
                    var avatar = n.avatarUrl
                        ? '<img src="' + escapeHtml(n.avatarUrl) + '" alt="" />'
                        : '<span class="material-symbols-outlined">person</span>';

                    html +=
                        '<div class="notif-item' + (n.unread ? ' unread' : '') + '" data-id="' + n.id + '">' +
                        '<div class="notif-avatar">' + avatar + '</div>' +
                        '<div class="notif-body">' +
                        '<p class="notif-text"><strong>' + escapeHtml(n.name) + '</strong> ' + escapeHtml(n.message) + '</p>' +
                        '<span class="notif-time">' + escapeHtml(n.time) + '</span>' +
                        '</div>' +
                        '<button class="notif-dismiss" type="button" data-id="' + n.id + '" aria-label="Dismiss">' +
                        '<span class="material-symbols-outlined">close</span>' +
                        '</button>' +
                        '</div>';
                });
                notifList.innerHTML = html;

                // Clicking a notification (not the dismiss button) marks it read
                notifList.querySelectorAll(".notif-item").forEach(function (item) {
                    item.addEventListener("click", function (e) {
                        if (e.target.closest(".notif-dismiss")) return;
                        var id = parseInt(item.dataset.id, 10);
                        var notif = notifications.find(function (n) { return n.id === id; });
                        if (notif) {
                            notif.unread = false;
                            renderNotifications();
                        }
                    });
                });

                // Dismiss (×) removes the notification entirely
                notifList.querySelectorAll(".notif-dismiss").forEach(function (btn) {
                    btn.addEventListener("click", function (e) {
                        e.stopPropagation();
                        var id = parseInt(btn.dataset.id, 10);
                        notifications = notifications.filter(function (n) { return n.id !== id; });
                        renderNotifications();
                    });
                });
            }

            updateBadge();
        }

        function updateBadge() {
            if (!notifBadge) return;
            var count = unreadCount();
            if (count > 0) {
                notifBadge.textContent = count > 99 ? "99+" : String(count);
                notifBadge.classList.remove("hidden");
            } else {
                notifBadge.classList.add("hidden");
            }
        }

        function openPanel() {
            if (notifPanel) notifPanel.classList.add("open");
        }

        function closePanel() {
            if (notifPanel) notifPanel.classList.remove("open");
        }

        function togglePanel() {
            if (notifPanel && notifPanel.classList.contains("open")) {
                closePanel();
            } else {
                openPanel();
            }
        }

        function markAllRead() {
            notifications.forEach(function (n) { n.unread = false; });
            renderNotifications();
        }

        if (notifBtn) {
            notifBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                togglePanel();
            });
        }

        if (markAllBtn) {
            markAllBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                markAllRead();
            });
        }

        document.addEventListener("click", function (e) {
            if (notifPanel && notifPanel.classList.contains("open")) {
                if (!notifPanel.contains(e.target) && e.target !== notifBtn) {
                    closePanel();
                }
            }
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && notifPanel && notifPanel.classList.contains("open")) {
                closePanel();
            }
        });

        // Initial render (renders the empty state)
        renderNotifications();

        // ── Public API for wiring real notifications in later ──
        // Example:
        // NotificationsPanel.add({ name: 'Rogimer Urriza', avatarUrl: '/images/employee.jpg',
        //   message: "Due soon: assignment '01 Activity 1 - ARG'", time: '2:46 am' });
        window.NotificationsPanel = {
            add: function (n) {
                notifications.unshift({
                    id: Date.now(),
                    name: n.name || "",
                    avatarUrl: n.avatarUrl || null,
                    message: n.message || "",
                    time: n.time || "Just now",
                    unread: true
                });
                if (notifications.length > 30) notifications.length = 30;
                renderNotifications();
            },
            setAll: function (list) {
                notifications = list;
                renderNotifications();
            },
            markAllRead: markAllRead,
            clear: function () {
                notifications = [];
                renderNotifications();
            }
        };

        // ── Profile dropdown ──────────────────────────────
        var profileTrigger  = document.getElementById("profileTrigger");
        var profileDropdown = document.getElementById("profileDropdown");

        function openProfile() {
            if (profileDropdown) {
                profileDropdown.classList.add("open");
            if (profileTrigger)  
                profileTrigger.classList.add("active");
            }
        }

        function closeProfile() {
            if (profileDropdown) {
                profileDropdown.classList.remove("open");
            if (profileTrigger)
                profileTrigger.classList.remove("active");
            }
        }

        function toggleProfile() {
            if (profileDropdown && profileDropdown.classList.contains("open")) {
                closeProfile();
            } else {
                if (notifPanel && notifPanel.classList.contains("open")) {
                    closePanel();
                }
                openProfile();
            }
        }

        if (profileTrigger) {
            profileTrigger.addEventListener("click", function (e) {
                e.stopPropagation();
                toggleProfile();
            });
        }

        document.addEventListener("click", function (e) {
            if (profileDropdown && profileDropdown.classList.contains("open")) {
                if (!profileDropdown.contains(e.target) && e.target !== profileTrigger && !profileTrigger.contains(e.target)) {
                    closeProfile();
                }
            }
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && profileDropdown && profileDropdown.classList.contains("open")) {
                closeProfile();
            }
        });

    });

})();