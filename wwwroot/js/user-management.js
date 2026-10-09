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
        var searchClear = document.getElementById("userSearchClear");
        var noMatch = document.getElementById("userNoMatch");

        if (searchInput && table) {
            function applySearch() {
                var query = searchInput.value.toLowerCase().trim();
                var tbody = table.querySelector("tbody");
                if (!tbody) return;

                var rows = tbody.querySelectorAll("tr.um-row, tr.um-row-disabled");
                var visibleCount = 0;

                rows.forEach(function (row) {
                    var searchableText = row.dataset.search || "";

                    if (query === "" || searchableText.indexOf(query) !== -1) {
                        row.classList.remove("um-search-hidden");
                        visibleCount++;
                    } else {
                        row.classList.add("um-search-hidden");
                    }
                });

                if (noMatch) noMatch.hidden = visibleCount !== 0 || query === "";
                if (searchClear) searchClear.hidden = query === "";
                currentPage = 1;
                paginate();

                // Update total badge and pagination info
                if (totalCount) {
                    totalCount.textContent = visibleCount + " User" + (visibleCount !== 1 ? "s" : "");
                }
            }
            searchInput.addEventListener("input", applySearch);
            if (searchClear) searchClear.addEventListener("click", function () {
                searchInput.value = "";
                applySearch();
                searchInput.focus();
            });
        }

        // ── Pagination ─────────────────────────────────────
        var rowsPerPage = 6;
        var currentPage = 1;
        var paginationBtns = document.getElementById("paginationBtns");

        function getAllVisibleRows() {
            if (!table) return [];
            var tbody = table.querySelector("tbody");
            if (!tbody) return [];
            return Array.from(tbody.querySelectorAll("tr.um-row, tr.um-row-disabled"))
                .filter(function (row) { return !row.classList.contains("um-search-hidden"); });
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

            // Update info text, e.g. "Showing 1–6 of 8 accounts"
            if (paginationInfo) {
                paginationInfo.textContent = totalRows === 0
                    ? "No accounts to show"
                    : "Showing " + (start + 1) + "–" + Math.min(end, totalRows) + " of " + totalRows +
                      " account" + (totalRows !== 1 ? "s" : "");
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

        // ── Six rows fill the accounts area ───────────────
        // Side by side, the table panel is as tall as the form, so the row
        // height is worked out to make exactly six rows fill it. The same
        // height (and frame) is used on every page, so pages line up.
        var umLayout = document.querySelector(".um-layout");
        var formPane = document.querySelector(".um-form-pane");
        var tablePane = document.querySelector(".um-table-pane");
        var tableFrame = tablePane ? tablePane.querySelector(".um-table-scroll") : null;
        var BASE_ROW = 64;

        function fitRows() {
            if (!umLayout || !formPane || !tablePane || !tableFrame || !table) return;
            var thead = table.querySelector("thead");
            table.style.removeProperty("--um-row-h");
            tableFrame.style.minHeight = "";
            if (window.innerWidth < 768 || !thead) return; // phones use cards

            var sideBySide = getComputedStyle(umLayout).gridTemplateColumns.trim().split(/\s+/).length > 1;
            var rowH = BASE_ROW;

            if (sideBySide) {
                // Measure both panels at their natural height.
                umLayout.style.alignItems = "start";
                var target = Math.max(formPane.getBoundingClientRect().height, tablePane.getBoundingClientRect().height);
                var fixed = 0;
                Array.prototype.forEach.call(tablePane.children, function (el) {
                    if (el !== tableFrame) fixed += el.getBoundingClientRect().height;
                });
                umLayout.style.alignItems = "";
                var available = target - fixed - thead.getBoundingClientRect().height;
                rowH = Math.max(BASE_ROW, Math.floor(available / rowsPerPage));
            }

            table.style.setProperty("--um-row-h", rowH + "px");
            // Borders add to each row; take them back out so six rows fit exactly.
            var sample = table.querySelector("tbody tr.um-row:not(.um-row-hidden):not(.um-search-hidden)");
            if (sample) {
                var extra = Math.round(sample.getBoundingClientRect().height) - rowH;
                if (extra > 0 && rowH - extra >= BASE_ROW) {
                    rowH -= extra;
                    table.style.setProperty("--um-row-h", rowH + "px");
                }
                var rowActual = sample.getBoundingClientRect().height;
                tableFrame.style.minHeight = Math.floor(thead.getBoundingClientRect().height + rowActual * rowsPerPage) + "px";
            }
        }

        var fitQueued = false;
        function queueFit() {
            if (fitQueued) return;
            fitQueued = true;
            requestAnimationFrame(function () { fitQueued = false; fitRows(); });
        }
        fitRows();
        window.addEventListener("resize", queueFit);
        if (document.fonts && document.fonts.ready) document.fonts.ready.then(queueFit);
        document.querySelectorAll(".um-form-pane select").forEach(function (el) {
            el.addEventListener("change", queueFit); // role help text changes the form height
        });

        // ── Notices: the × only removes the message from the page ──
        // (nothing is sent to the server). The panels change height, so refit.
        document.addEventListener("click", function (e) {
            var btn = e.target.closest(".um-notice-close");
            if (!btn) return;
            var notice = btn.closest(".um-notice");
            if (notice) notice.remove();
            queueFit();
        });

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
                // Not allowed while they still have unfinished work.
                var blocked = original.openTasks > 0;
                csSave.disabled = !changed || blocked;
                csPreview.hidden = !changed;
                csHint.hidden = changed || blocked;
                document.getElementById("csBefore").textContent = describe(original.role, original.department);
                document.getElementById("csAfter").textContent = describe(role, department);

                csWarnings.innerHTML = "";
                if (blocked) {
                    var li = document.createElement("li");
                    li.textContent = original.name + " still has " + original.openTasks +
                        " unfinished task(s). Reassign or finish them before changing their role or division.";
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

// ── Deactivate / Reactivate dialog ────────────────────────
// Replaces the browser's alert()/confirm() boxes. Three cases: deactivation
// refused because of unfinished tasks, confirm deactivate, confirm reactivate.
(function () {
    document.addEventListener("DOMContentLoaded", function () {
        var dialog = document.getElementById("statusDialog");
        if (!dialog) return;

        var icon = document.getElementById("sdIcon");
        var title = document.getElementById("sdTitle");
        var initials = document.getElementById("sdInitials");
        var userName = document.getElementById("sdUserName");
        var message = document.getElementById("sdMessage");
        var points = document.getElementById("sdPoints");
        var cancelBtn = document.getElementById("sdCancel");
        var confirmBtn = document.getElementById("sdConfirm");
        var tasksLink = document.getElementById("sdTasksLink");
        var pendingForm = null;

        var modes = {
            blocked: {
                tone: "warning", icon: "warning", title: "Can't deactivate yet",
                cancel: "Close", confirm: null
            },
            deactivate: {
                tone: "danger", icon: "person_off", title: "Deactivate this account?",
                cancel: "Cancel", confirm: "Deactivate account", busy: "Deactivating...",
                points: [
                    "They won't be able to sign in.",
                    "Their past tasks and records are kept.",
                    "You can reactivate the account anytime."
                ]
            },
            reactivate: {
                tone: "success", icon: "person_check", title: "Reactivate this account?",
                cancel: "Cancel", confirm: "Reactivate account", busy: "Reactivating...",
                points: ["They will be able to sign in again."]
            }
        };

        function open(form) {
            var name = form.dataset.userName || "This person";
            var openTasks = parseInt(form.dataset.openTasks, 10) || 0;
            var key = form.dataset.action === "deactivate" && openTasks > 0 ? "blocked" : form.dataset.action;
            var mode = modes[key];
            pendingForm = mode.confirm ? form : null;

            dialog.dataset.tone = mode.tone;
            icon.textContent = mode.icon;
            title.textContent = mode.title;
            userName.textContent = name;
            initials.textContent = name.split(" ").filter(Boolean)
                .map(function (w) { return w[0]; }).slice(0, 2).join("").toUpperCase();

            if (key === "blocked") {
                message.textContent = name + " still has " + openTasks + " unfinished task"
                    + (openTasks === 1 ? "" : "s")
                    + ". Reassign " + (openTasks === 1 ? "it" : "them")
                    + " to someone else or wait until " + (openTasks === 1 ? "it's" : "they're")
                    + " finished, then try again.";
                message.hidden = false;
            } else {
                message.hidden = true;
            }

            points.textContent = "";
            (mode.points || []).forEach(function (text) {
                var li = document.createElement("li");
                li.textContent = text;
                points.appendChild(li);
            });
            points.hidden = !mode.points;

            cancelBtn.textContent = mode.cancel;
            confirmBtn.hidden = !mode.confirm;
            confirmBtn.disabled = false;
            confirmBtn.textContent = mode.confirm || "";
            confirmBtn.dataset.busy = mode.busy || "";
            tasksLink.hidden = key !== "blocked";
            tasksLink.href = form.dataset.tasksUrl || "#";

            dialog.showModal();
            (mode.confirm ? cancelBtn : tasksLink).focus();
        }

        document.querySelectorAll("form.um-status-form").forEach(function (form) {
            form.addEventListener("submit", function (e) {
                e.preventDefault();
                open(form);
            });
        });

        // form.submit() skips the submit event, so the dialog doesn't reopen.
        confirmBtn.addEventListener("click", function () {
            if (!pendingForm) return;
            confirmBtn.disabled = true;
            confirmBtn.textContent = confirmBtn.dataset.busy;
            pendingForm.submit();
        });

        function closeDialog() { dialog.close(); }
        cancelBtn.addEventListener("click", closeDialog);
        document.getElementById("sdClose").addEventListener("click", closeDialog);
        dialog.addEventListener("click", function (e) {
            if (e.target === dialog) closeDialog();
        });
    });
})();

// ── Edit Email dialog ─────────────────────────────────────
(function () {
    document.addEventListener("DOMContentLoaded", function () {
        var dialog = document.getElementById("editEmailDialog");
        if (!dialog) return;
        var emailInput = document.getElementById("eeEmail");

        document.querySelectorAll(".um-btn-edit-email").forEach(function (btn) {
            btn.addEventListener("click", function () {
                document.getElementById("eeUserId").value = btn.dataset.userId;
                document.getElementById("eeUserName").textContent = btn.dataset.userName;
                emailInput.value = btn.dataset.userEmail || "";
                dialog.showModal();
                emailInput.focus();
                emailInput.select();
            });
        });

        function closeDialog() { dialog.close(); }
        document.getElementById("eeCancel").addEventListener("click", closeDialog);
        document.getElementById("eeClose").addEventListener("click", closeDialog);
        dialog.addEventListener("click", function (e) {
            if (e.target === dialog) closeDialog();
        });
        document.getElementById("eeForm").addEventListener("submit", function () {
            var save = document.getElementById("eeSave");
            save.disabled = true;
            save.textContent = "Saving...";
        });
    });
})();
