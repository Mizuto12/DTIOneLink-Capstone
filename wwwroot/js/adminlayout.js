// ==========================================================
// DTI Laguna OneLink — Admin Shell JS
// (shared by AdminLayout.cshtml, SuperAdminLayout.cshtml, EmployeeLayout.cshtml)
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

        // ── Notifications ──────────────────────────────────
        var notifBtn   = document.getElementById("notifBtn");
        var notifPanel = document.getElementById("notifPanel");
        var notifBadge = document.getElementById("notifBadge");
        var notifList  = document.getElementById("notifList");
        var markAllBtn = document.getElementById("markAllRead");

        // Loaded from /Notifications/List — never seeded locally.
        // { id, message, time, unread, link }
        var notifications = [];

        function getCsrfToken() {
            var input = document.querySelector('input[name="__RequestVerificationToken"]');
            return input ? input.value : "";
        }

        function unreadCount() {
            return notifications.filter(function (n) { return n.unread; }).length;
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

        // Builds one notification row using DOM APIs only. All user-controlled
        // text (message, time) is set via textContent, never innerHTML — a
        // notification message can never be interpreted as markup.
        function buildNotifItem(n) {
            var item = document.createElement("div");
            item.className = "notif-item" + (n.unread ? " unread" : "");
            item.dataset.id = String(n.id);

            var avatar = document.createElement("div");
            avatar.className = "notif-avatar";
            var avatarIcon = document.createElement("span");
            avatarIcon.className = "material-symbols-outlined";
            avatarIcon.textContent = "notifications";
            avatar.appendChild(avatarIcon);

            var body = document.createElement("div");
            body.className = "notif-body";

            var text = document.createElement("p");
            text.className = "notif-text";
            text.textContent = n.message;
            body.appendChild(text);

            var time = document.createElement("span");
            time.className = "notif-time";
            time.textContent = n.time;
            body.appendChild(time);

            var dismissBtn = document.createElement("button");
            dismissBtn.className = "notif-dismiss";
            dismissBtn.type = "button";
            dismissBtn.setAttribute("aria-label", "Dismiss");
            var dismissIcon = document.createElement("span");
            dismissIcon.className = "material-symbols-outlined";
            dismissIcon.textContent = "close";
            dismissBtn.appendChild(dismissIcon);

            item.appendChild(avatar);
            item.appendChild(body);
            item.appendChild(dismissBtn);

            dismissBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                dismissNotification(n.id);
            });

            item.addEventListener("click", function (e) {
                if (e.target.closest(".notif-dismiss")) return;
                openNotification(n.id);
            });

            return item;
        }

        function renderNotifications() {
            if (!notifList) return;
            notifList.textContent = ""; // clears safely — no innerHTML

            if (notifications.length === 0) {
                var empty = document.createElement("div");
                empty.className = "notif-empty";
                var icon = document.createElement("span");
                icon.className = "material-symbols-outlined";
                icon.textContent = "notifications_off";
                var p = document.createElement("p");
                p.textContent = "No current notifications";
                empty.appendChild(icon);
                empty.appendChild(p);
                notifList.appendChild(empty);
            } else {
                notifications.forEach(function (n) {
                    notifList.appendChild(buildNotifItem(n));
                });
            }

            updateBadge();
        }

        function markReadOnServer(id) {
            return fetch("/Notifications/MarkRead?id=" + encodeURIComponent(id), {
                method: "POST",
                headers: { "X-CSRF-TOKEN": getCsrfToken() }
            });
        }

        function openNotification(id) {
            var notif = notifications.find(function (n) { return n.id === id; });
            if (!notif) return;

            if (notif.unread) {
                notif.unread = false;
                renderNotifications();
                markReadOnServer(id).catch(function () { /* local state already updated */ });
            }

            // Only navigate to a same-origin relative path ("/something"), never
            // "//host/..." (protocol-relative) or an absolute external URL — a
            // notification's Link is treated as untrusted data, not a trusted redirect.
            var target = notif.link;
            if (target && target.indexOf("/") === 0 && target.indexOf("//") !== 0) {
                window.location.href = target;
            }
        }

        function dismissNotification(id) {
            notifications = notifications.filter(function (n) { return n.id !== id; });
            renderNotifications();
            fetch("/Notifications/Dismiss?id=" + encodeURIComponent(id), {
                method: "POST",
                headers: { "X-CSRF-TOKEN": getCsrfToken() }
            }).catch(function () { /* local state already updated */ });
        }

        function markAllRead() {
            notifications.forEach(function (n) { n.unread = false; });
            renderNotifications();
            fetch("/Notifications/MarkAllRead", {
                method: "POST",
                headers: { "X-CSRF-TOKEN": getCsrfToken() }
            }).catch(function () { /* local state already updated */ });
        }

        function loadNotifications() {
            fetch("/Notifications/List")
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    notifications = data.map(function (n) {
                        return {
                            id: n.id,
                            message: n.text,
                            time: n.time,
                            unread: n.unread,
                            link: n.link || null
                        };
                    });
                    renderNotifications();
                })
                .catch(function () {
                    renderNotifications(); // shows the empty state on failure
                });
        }

        function openPanel() {
            if (notifPanel) {
                notifPanel.classList.add("open");
                if (notifBtn) notifBtn.setAttribute("aria-expanded", "true");
            }
        }

        function closePanel() {
            if (notifPanel) {
                notifPanel.classList.remove("open");
                if (notifBtn) notifBtn.setAttribute("aria-expanded", "false");
            }
        }

        function togglePanel() {
            if (notifPanel && notifPanel.classList.contains("open")) {
                closePanel();
            } else {
                openPanel();
            }
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
                if (!notifPanel.contains(e.target) && e.target !== notifBtn && !(notifBtn && notifBtn.contains(e.target))) {
                    closePanel();
                }
            }
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && notifPanel && notifPanel.classList.contains("open")) {
                closePanel();
            }
        });

        renderNotifications(); // empty state immediately
        loadNotifications();   // then fill in from the database

        // ── Profile dropdown (unchanged) ───────────────────
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