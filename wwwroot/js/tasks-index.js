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

        // ── Filters ────────────────────────────────────────
        // The toolbar is a plain GET form; dropdowns apply as soon as they
        // change (search applies on Enter). Changing a filter always goes
        // back to page 1, since the form doesn't carry a page value.
        var filterForm = document.getElementById("taskFilterForm");
        if (filterForm) {
            filterForm.querySelectorAll("select[data-autosubmit]").forEach(function (select) {
                select.addEventListener("change", function () {
                    filterForm.submit();
                });
            });

            // Don't put empty / default values in the URL.
            filterForm.addEventListener("formdata", function (event) {
                var defaults = { q: "", status: "all", priority: "all", sort: "newest", due: "all", department: "", employeeId: "" };
                Object.keys(defaults).forEach(function (key) {
                    if (event.formData.get(key) === defaults[key]) {
                        event.formData.delete(key);
                    }
                });
            });
        }

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
