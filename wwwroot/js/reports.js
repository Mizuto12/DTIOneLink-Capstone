// ==========================================================
// DTI Laguna OneLink — Reports JS
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Filter dropdown toggle ────────────────────────
        var filterBtn = document.getElementById("filterBtn");
        var dropdown = document.getElementById("filterDropdown");
        var currentFilter = document.getElementById("currentFilter");

        if (filterBtn && dropdown) {
            filterBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                dropdown.classList.toggle("hidden");
            });

            dropdown.querySelectorAll(".rp-dropdown-item").forEach(function (item) {
                item.addEventListener("click", function () {
                    dropdown.querySelectorAll(".rp-dropdown-item").forEach(function (i) {
                        i.classList.remove("rp-dropdown-active");
                    });
                    item.classList.add("rp-dropdown-active");
                    if (currentFilter) {
                        currentFilter.textContent = item.textContent;
                    }
                    dropdown.classList.add("hidden");
                });
            });
        }

        // Close dropdown when clicking outside
        document.addEventListener("click", function (e) {
            if (dropdown && !dropdown.classList.contains("hidden")) {
                if (!filterBtn.contains(e.target) && !dropdown.contains(e.target)) {
                    dropdown.classList.add("hidden");
                }
            }
        });

        // ── Search input micro-interaction ─────────────────
        var searchInput = document.getElementById("reportSearch");
        if (searchInput) {
            searchInput.addEventListener("focus", function () {
                searchInput.parentElement.style.transform = "scale(1.01)";
                searchInput.parentElement.style.transition = "transform 0.2s";
            });
            searchInput.addEventListener("blur", function () {
                searchInput.parentElement.style.transform = "scale(1)";
            });
        }

    });

})();
