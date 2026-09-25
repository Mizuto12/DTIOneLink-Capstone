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

        // ── Role dropdowns: show what the chosen role means under the box ──
        document.querySelectorAll(".um-role-select").forEach(function (select) {
            var help = document.getElementById(select.dataset.help);
            function showHelp() {
                var option = select.options[select.selectedIndex];
                help.textContent = option && option.dataset.help ? option.dataset.help : "";
            }
            select.addEventListener("change", showHelp);
            select.showHelp = showHelp;
            showHelp();
        });

        // ── Change role / division dialog (OPD only) ──────
        var csDialog = document.getElementById("changeStandingDialog");
        if (csDialog) {
            var OPD = "Office of the Provincial Director";
            var roleNames = { Employee: "Employee", Admin: "Admin", SuperAdmin: "Super Admin" };
            var csRole = document.getElementById("csRole");
            var csDivision = document.getElementById("csDivision");
            var csDepartment = document.getElementById("csDepartment"); // hidden, posted
            var csOpdNote = document.getElementById("csOpdNote");
            var csPreview = document.getElementById("csPreview");
            var csHint = document.getElementById("csHint");
            var csWarnings = document.getElementById("csWarnings");
            var csSave = document.getElementById("csSave");
            var original = { name: "", role: "", department: "", openTasks: 0 };

            function describe(role, department) {
                return (roleNames[role] || role) + " · " + (department || "No division");
            }

            // Before -> after preview; Save stays off until something changes.
            function update() {
                var role = csRole.value;
                // A Super Admin always belongs to the OPD.
                if (role === "SuperAdmin") csDivision.value = OPD;
                csDivision.disabled = role === "SuperAdmin";
                csOpdNote.hidden = role !== "SuperAdmin";
                var department = csDivision.value;
                csDepartment.value = department;

                var changed = role !== original.role || department !== original.department;
                csSave.disabled = !changed;
                csPreview.hidden = !changed;
                csHint.hidden = changed;
                document.getElementById("csBefore").textContent = describe(original.role, original.department);
                document.getElementById("csAfter").textContent = describe(role, department);

                csWarnings.innerHTML = "";
                if (changed && original.openTasks > 0) {
                    var li = document.createElement("li");
                    li.textContent = original.name + " still has " + original.openTasks +
                        " unfinished task(s). They stay as they are; reassign them if needed.";
                    csWarnings.appendChild(li);
                }
            }

            document.querySelectorAll(".um-btn-change").forEach(function (btn) {
                btn.addEventListener("click", function () {
                    original = {
                        name: btn.dataset.userName,
                        role: btn.dataset.userRole,
                        department: btn.dataset.userDepartment,
                        openTasks: parseInt(btn.dataset.openTasks || "0", 10)
                    };
                    document.getElementById("csUserId").value = btn.dataset.userId;
                    document.getElementById("csUserName").textContent = original.name;
                    var initials = document.getElementById("csInitials");
                    if (initials) {
                        initials.textContent = original.name.split(/\s+/).filter(Boolean)
                            .map(function (w) { return w[0]; }).slice(0, 2).join("").toUpperCase();
                    }
                    document.getElementById("csCurrent").textContent = describe(original.role, original.department);
                    csRole.value = original.role;
                    csDivision.value = original.department;
                    csRole.showHelp();
                    csSave.textContent = "Save changes";
                    update();
                    csDialog.showModal();
                });
            });

            csRole.addEventListener("change", update);
            csDivision.addEventListener("change", update);

            function closeDialog() { csDialog.close(); }
            document.getElementById("csCancel").addEventListener("click", closeDialog);
            document.getElementById("csClose").addEventListener("click", closeDialog);
            // Clicking the dark area outside the box also closes it.
            csDialog.addEventListener("click", function (e) {
                if (e.target === csDialog) closeDialog();
            });
            document.getElementById("csForm").addEventListener("submit", function () {
                csSave.disabled = true;
                csSave.textContent = "Saving...";
            });
        }

        // ── Instant "already exists" check on the Add form ─
        // The server checks again on save; this only warns while typing,
        // using the accounts already listed in the table.
        var existingUsers = Array.from(document.querySelectorAll("#userTable tr.um-row")).map(function (row) {
            return {
                name: (row.dataset.name || "").trim().replace(/\s+/g, " ").toLowerCase(),
                email: (row.dataset.email || "").trim().toLowerCase(),
                displayName: row.dataset.name,
                displayEmail: row.dataset.email
            };
        });

        function watchDuplicate(inputId, errorId, match, message) {
            var input = document.getElementById(inputId);
            var error = document.getElementById(errorId);
            if (!input || !error) return function () { return false; };
            function check() {
                var value = input.value.trim().replace(/\s+/g, " ").toLowerCase();
                var found = value ? existingUsers.find(function (u) { return match(u, value); }) : null;
                input.classList.toggle("um-input-invalid", !!found);
                error.hidden = !found;
                if (found) error.textContent = message(found);
                return !!found;
            }
            input.addEventListener("input", check);
            return check;
        }

        var emailTaken = watchDuplicate("newUserEmail", "newUserEmailError",
            function (u, v) { return u.email === v; },
            function (u) { return "This email is already used by " + u.displayName + "."; });
        var nameTaken = watchDuplicate("newUserFullName", "newUserFullNameError",
            function (u, v) { return u.name === v; },
            function (u) { return "A user with this name already exists (" + u.displayEmail + ")."; });

        if (form) {
            // Capture phase, so this runs before the "PROCESSING..." handler
            // and a blocked save doesn't leave the button stuck.
            form.addEventListener("submit", function (e) {
                var dupEmail = emailTaken();
                var dupName = nameTaken();
                if (dupEmail || dupName) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    document.getElementById(dupEmail ? "newUserEmail" : "newUserFullName").focus();
                }
            }, true);
        }

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
