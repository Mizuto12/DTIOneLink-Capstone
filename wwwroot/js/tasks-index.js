// ==========================================================
// DTI Laguna OneLink — Task Management Index JS
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Table row click micro-interaction ─────────────
        document.querySelectorAll(".task-row").forEach(function (row) {
            row.addEventListener("click", function () {
                row.style.transform = "scale(0.98)";
                setTimeout(function () {
                    row.style.transform = "";
                }, 150);
            });
        });

        // ── Search bar subtle focus effect (no pop-up) ────
        var searchInput = document.querySelector(".search-input");
        if (searchInput) {
            searchInput.addEventListener("focus", function () {
                this.style.borderColor = "rgba(0,30,101,0.3)";
                this.style.boxShadow = "inset 0 0 0 2px rgba(0,30,101,0.1)";
            });
            searchInput.addEventListener("blur", function () {
                this.style.borderColor = "";
                this.style.boxShadow = "";
            });
        }

        // ── Pagination ─────────────────────────────────────
        document.querySelectorAll(".page-btn:not(.page-nav)").forEach(function (btn) {
            btn.addEventListener("click", function () {
                document.querySelectorAll(".page-btn").forEach(function (b) {
                    b.classList.remove("page-active");
                });
                btn.classList.add("page-active");
            });
        });

        // ── Animate mini bars on load ──────────────────────
        document.querySelectorAll(".mini-bar, .progress-fill").forEach(function (bar) {
            var target = bar.style.width;
            bar.style.width = "0%";
            setTimeout(function () {
                bar.style.width = target;
            }, 300);
        });

    });

})();
