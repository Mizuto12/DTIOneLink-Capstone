// ==========================================================
// DTI Laguna OneLink — Admin Shell JS
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
        var notifBtn   = document.getElementById("notifBtn");
        var notifPanel = document.getElementById("notifPanel");
        var notifDot   = document.getElementById("notifDot");
        var notifList  = document.getElementById("notifList");
        var markAllBtn = document.getElementById("markAllRead");

        // Seed with a few demo notifications
        var notifications = [
            { id: 1, type: "task", text: "New task assigned: \"Q3 Compliance Review\"", time: "5 min ago", unread: true },
            { id: 2, type: "user", text: "Juan Dela Cruz created a new account", time: "2 hours ago", unread: true },
            { id: 3, type: "system", text: "System maintenance scheduled for July 15", time: "1 day ago", unread: false }
        ];

        function hasUnread() {
            return notifications.some(function (n) { return n.unread; });
        }

        function renderNotifications() {
            if (!notifList) return;

            if (notifications.length === 0) {
                notifList.innerHTML =
                    '<div class="notif-empty">' +
                    '<span class="material-symbols-outlined">notifications_off</span>' +
                    '<p>No notifications yet</p>' +
                    '</div>';
            } else {
                var html = "";
                notifications.forEach(function (n) {
                    html +=
                        '<div class="notif-item' + (n.unread ? ' unread' : '') + '" data-id="' + n.id + '">' +
                        '<div class="notif-icon ' + n.type + '">' +
                        '<span class="material-symbols-outlined">' +
                        (n.type === "task" ? "assignment" : n.type === "user" ? "person_add" : "info") +
                        '</span></div>' +
                        '<div class="notif-body">' +
                        '<p class="notif-text">' + n.text + '</p>' +
                        '<span class="notif-time">' + n.time + '</span>' +
                        '</div></div>';
                });
                notifList.innerHTML = html;

                // Click on individual notification marks it as read
                notifList.querySelectorAll(".notif-item").forEach(function (item) {
                    item.addEventListener("click", function () {
                        var id = parseInt(item.dataset.id, 10);
                        var notif = notifications.find(function (n) { return n.id === id; });
                        if (notif) {
                            notif.unread = false;
                            item.classList.remove("unread");
                            updateDot();
                        }
                    });
                });
            }

            updateDot();
        }

        function updateDot() {
            if (notifDot) {
                if (hasUnread()) {
                    notifDot.classList.remove("hidden");
                } else {
                    notifDot.classList.add("hidden");
                }
            }
        }

        function openPanel() {
            if (notifPanel) {
                notifPanel.classList.add("open");
            }
        }

        function closePanel() {
            if (notifPanel) {
                notifPanel.classList.remove("open");
            }
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

        // Close panel when clicking outside
        document.addEventListener("click", function (e) {
            if (notifPanel && notifPanel.classList.contains("open")) {
                if (!notifPanel.contains(e.target) && e.target !== notifBtn) {
                    closePanel();
                }
            }
        });

        // Close on Escape
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && notifPanel && notifPanel.classList.contains("open")) {
                closePanel();
            }
        });

        // Initial render
        renderNotifications();

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
                profileTrigger.classList.remove("active"); // ← add this
            }
        }

        function toggleProfile() {
            if (profileDropdown && profileDropdown.classList.contains("open")) {
                closeProfile();
            } else {
                // Close notif panel if open, then open profile
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

        // Close profile when clicking outside
        document.addEventListener("click", function (e) {
            if (profileDropdown && profileDropdown.classList.contains("open")) {
                if (!profileDropdown.contains(e.target) && e.target !== profileTrigger && !profileTrigger.contains(e.target)) {
                    closeProfile();
                }
            }
        });

        // Close on Escape
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && profileDropdown && profileDropdown.classList.contains("open")) {
                closeProfile();
            }
        });

        // ── Demo: add a new notification every 60 seconds ──
        var demoId = 100;
        setInterval(function () {
            var types = ["task", "user", "system"];
            var texts = [
                "Reminder: Submit monthly report by Friday",
                "New user registration pending approval",
                "Database backup completed successfully",
                "Task \"Policy Review\" is overdue"
            ];
            notifications.unshift({
                id: demoId++,
                type: types[Math.floor(Math.random() * types.length)],
                text: texts[Math.floor(Math.random() * texts.length)],
                time: "Just now",
                unread: true
            });
            // Keep max 20 notifications
            if (notifications.length > 20) notifications.length = 20;
            renderNotifications();
        }, 60000);

    });

})();
