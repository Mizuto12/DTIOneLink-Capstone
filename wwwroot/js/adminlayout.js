// ==========================================================
// DTI Laguna OneLink — Admin Shell JS
// Extracted from the original HTML, no Tailwind dependency.
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Table row click ripple ─────────────────────────
        // Adds a brief opacity flash when a table row is clicked.
        // Works on any .table-row-track elements inside @RenderBody().
        document.querySelectorAll(".table-row-track").forEach(function (row) {
            row.addEventListener("click", function () {
                row.style.opacity = "0.5";
                setTimeout(function () {
                    row.style.opacity = "";
                }, 200);
            });
        });

        // ── Stat counter animation ─────────────────────────
        // Counts up any .stat-counter element from its data-start
        // attribute to its data-end attribute over 2 seconds.
        // Usage in view: <span class="stat-counter" data-start="1200" data-end="1284"></span>
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

        // ── Notification button placeholder ───────────────
        var notifBtn = document.querySelector(".notif-btn");
        if (notifBtn) {
            notifBtn.addEventListener("click", function () {
                // TODO: open notification panel or redirect to /Notifications
                console.log("Notifications clicked.");
            });
        }

    });

})();
