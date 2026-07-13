// ==========================================================
// DTI Laguna OneLink — User Management JS
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Input / select focus ring effect ──────────────
        document.querySelectorAll(".um-input, .um-select").forEach(function (el) {
            el.addEventListener("focus", function () {
                var wrap = el.closest(".um-field-group");
                if (wrap) wrap.style.filter = "drop-shadow(0 0 6px rgba(0,30,101,0.08))";
            });
            el.addEventListener("blur", function () {
                var wrap = el.closest(".um-field-group");
                if (wrap) wrap.style.filter = "";
            });
        });

        // ── Form submit animation ─────────────────────────
        var form = document.getElementById("addUserForm");
        var saveBtn = document.getElementById("saveBtn");

        if (form && saveBtn) {
            form.addEventListener("submit", function () {
                saveBtn.innerHTML =
                    '<span class="material-symbols-outlined" style="animation:spin 0.8s linear infinite">sync</span>' +
                    ' PROCESSING...';
                saveBtn.disabled = true;
                saveBtn.style.opacity = "0.8";
                saveBtn.style.cursor  = "not-allowed";
            });
        }

        // ── Search filtering ──────────────────────────────
        var searchInput = document.getElementById("userSearchInput");
        var table = document.getElementById("userTable");
        var totalCount = document.getElementById("totalUsersCount");
        var paginationInfo = document.getElementById("paginationInfo");

        if (searchInput && table) {
            searchInput.addEventListener("input", function () {
                var query = searchInput.value.toLowerCase().trim();
                var tbody = table.querySelector("tbody");
                if (!tbody) return;

                var rows = tbody.querySelectorAll("tr.um-row, tr.um-row-disabled");
                var visibleCount = 0;

                rows.forEach(function (row) {
                    var nameCell = row.querySelector(".um-name-cell span");
                    var emailCell = row.querySelector(".um-td-muted");
                    var nameText = nameCell ? nameCell.textContent.toLowerCase() : "";
                    var emailText = emailCell ? emailCell.textContent.toLowerCase() : "";

                    if (query === "" || nameText.indexOf(query) !== -1 || emailText.indexOf(query) !== -1) {
                        row.classList.remove("um-row-hidden");
                        visibleCount++;
                    } else {
                        row.classList.add("um-row-hidden");
                    }
                });

                // Update total badge and pagination info
                if (totalCount) {
                    totalCount.textContent = visibleCount + " User" + (visibleCount !== 1 ? "s" : "");
                }
                if (paginationInfo) {
                    paginationInfo.textContent = "Showing " + visibleCount + " of " + rows.length + " entries";
                }
            });
        }

        // ── Pagination ─────────────────────────────────────
        var rowsPerPage = 4;
        var currentPage = 1;
        var paginationBtns = document.getElementById("paginationBtns");

        function getAllVisibleRows() {
            if (!table) return [];
            var tbody = table.querySelector("tbody");
            if (!tbody) return [];
            return Array.from(tbody.querySelectorAll("tr.um-row, tr.um-row-disabled"));
        }

        function paginate() {
            var allRows = getAllVisibleRows();
            var totalRows = allRows.length;
            var totalPages = Math.ceil(totalRows / rowsPerPage) || 1;

            if (currentPage > totalPages) currentPage = totalPages;

            var start = (currentPage - 1) * rowsPerPage;
            var end = start + rowsPerPage;

            allRows.forEach(function (row, i) {
                if (i >= start && i < end) {
                    row.classList.remove("um-row-hidden");
                } else {
                    row.classList.add("um-row-hidden");
                }
            });

            // Update info text
            var showing = totalRows === 0 ? 0 : Math.min(rowsPerPage, totalRows - start);
            if (paginationInfo) {
                paginationInfo.textContent = "Showing " + showing + " of " + totalRows + " entries";
            }

            // Render page buttons
            renderPageButtons(totalPages);
        }

        function renderPageButtons(totalPages) {
            if (!paginationBtns) return;

            var html = "";

            // Prev
            html += '<button class="um-page-nav" data-page="prev" ' + (currentPage <= 1 ? "disabled" : "") + '>';
            html += '<span class="material-symbols-outlined">chevron_left</span></button>';

            // Page numbers
            for (var p = 1; p <= totalPages; p++) {
                html += '<button class="um-page-btn' + (p === currentPage ? " um-page-active" : "") + '" data-page="' + p + '">' + p + '</button>';
            }

            // Next
            html += '<button class="um-page-nav" data-page="next" ' + (currentPage >= totalPages ? "disabled" : "") + '>';
            html += '<span class="material-symbols-outlined">chevron_right</span></button>';

            paginationBtns.innerHTML = html;

            // Bind click events
            paginationBtns.querySelectorAll(".um-page-btn, .um-page-nav").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    var page = btn.getAttribute("data-page");
                    if (page === "prev") {
                        currentPage = Math.max(1, currentPage - 1);
                    } else if (page === "next") {
                        currentPage = Math.min(totalPages, currentPage + 1);
                    } else {
                        currentPage = parseInt(page, 10);
                    }
                    paginate();
                });
            });
        }

        // Initial pagination
        paginate();

        // Hook into search to reset page and re-paginate
        if (searchInput) {
            searchInput.addEventListener("input", function () {
                currentPage = 1;
            });
        }

    });

    // spin keyframe injected by JS (avoids adding it to global CSS)
    var style = document.createElement("style");
    style.textContent = "@keyframes spin { from { transform:rotate(0deg); } to { transform:rotate(360deg); } }";
    document.head.appendChild(style);

})();
