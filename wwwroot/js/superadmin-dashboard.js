// ==========================================================
// DTI Laguna OneLink — Super Admin Dashboard
// Division chips on the Task Board: show one division's cards
// (or all of them) and keep each column's count in step.
// Also: pagination for the Overdue Tasks and Employee Workload
// tables, so a busy office's full list doesn't dump hundreds of
// rows onto the page at once.
// ==========================================================
(function () {
    "use strict";

    var PAGE_SIZE = 10;

    // Generic row-set pager: give it the pager's DOM pieces once, then feed
    // it whichever rows are currently eligible to show (setItems) — it
    // decides which of those fit on the current page. A caller with its own
    // filter (e.g. the Workload division chips) hides ineligible rows itself
    // and only hands the paginator the rows that passed the filter, so the
    // two never fight over the same row's hidden state.
    function makePager(container, infoEl, prevBtn, nextBtn) {
        var page = 0;
        var items = [];

        function render() {
            var total = items.length;
            if (total <= PAGE_SIZE) {
                container.hidden = true;
                items.forEach(function (row) { row.hidden = false; });
                return;
            }

            var pageCount = Math.ceil(total / PAGE_SIZE);
            if (page >= pageCount) page = pageCount - 1;
            if (page < 0) page = 0;

            var start = page * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            items.forEach(function (row, i) { row.hidden = !(i >= start && i < end); });

            container.hidden = false;
            infoEl.textContent = "Showing " + (start + 1) + "–" + end + " of " + total;
            prevBtn.disabled = page === 0;
            nextBtn.disabled = page === pageCount - 1;
        }

        prevBtn.addEventListener("click", function () {
            if (page > 0) { page--; render(); }
        });
        nextBtn.addEventListener("click", function () {
            var pageCount = Math.ceil(items.length / PAGE_SIZE);
            if (page < pageCount - 1) { page++; render(); }
        });

        return {
            setItems: function (newItems) {
                items = newItems;
                page = 0;
                render();
            },
        };
    }

    document.addEventListener("DOMContentLoaded", function () {
        var board = document.getElementById("saBoard");
        if (!board) return;

        var chips = Array.from(board.querySelectorAll(".sa-chip"));
        var columns = Array.from(board.querySelectorAll(".sa-col"));

        function apply(division) {
            chips.forEach(function (chip) {
                var on = chip.dataset.division === division;
                chip.classList.toggle("is-active", on);
                chip.setAttribute("aria-pressed", on ? "true" : "false");
            });

            columns.forEach(function (col) {
                var shown = 0;
                col.querySelectorAll(".sa-kcard").forEach(function (card) {
                    var match = !division || card.dataset.division === division;
                    card.hidden = !match;
                    if (match) shown++;
                });
                var count = col.querySelector("[data-count]");
                if (count) count.textContent = String(shown);
                var empty = col.querySelector(".sa-col__empty");
                if (empty) empty.hidden = shown > 0;
            });

            // Remember the choice for this tab, so live refreshes keep it.
            try { sessionStorage.setItem(STORAGE_KEY, division); } catch (e) { /* storage blocked */ }
        }

        var STORAGE_KEY = "saBoardDivision";
        chips.forEach(function (chip) {
            chip.addEventListener("click", function () { apply(chip.dataset.division); });
        });

        var saved = "";
        try { saved = sessionStorage.getItem(STORAGE_KEY) || ""; } catch (e) { /* storage blocked */ }
        if (saved && chips.some(function (c) { return c.dataset.division === saved; })) {
            apply(saved);
        }
    });

    // Overdue Tasks table: no filter, just paginate the full row list.
    document.addEventListener("DOMContentLoaded", function () {
        var table = document.getElementById("saOverdueTable");
        var pagerEl = document.getElementById("saOverduePager");
        var infoEl = document.getElementById("saOverduePagerInfo");
        var prevBtn = document.getElementById("saOverduePrev");
        var nextBtn = document.getElementById("saOverdueNext");
        if (!table || !pagerEl || !infoEl || !prevBtn || !nextBtn) return;

        var rows = Array.from(table.querySelectorAll("tbody tr"));
        var pager = makePager(pagerEl, infoEl, prevBtn, nextBtn);
        pager.setItems(rows);
    });

    // Employee Workload table: same chip pattern as the board, filtering
    // table rows — now handing the surviving (division-matched) rows to the
    // pager instead of showing all of them at once.
    document.addEventListener("DOMContentLoaded", function () {
        var section = document.getElementById("saWorkload");
        if (!section) return;

        var chips = Array.from(section.querySelectorAll(".sa-chip"));
        var rows = Array.from(section.querySelectorAll("tbody tr[data-division]"));
        var emptyRow = section.querySelector(".sa-table__empty");
        if (!chips.length || !rows.length) return;

        var pagerEl = document.getElementById("saWorkloadPager");
        var infoEl = document.getElementById("saWorkloadPagerInfo");
        var prevBtn = document.getElementById("saWorkloadPrev");
        var nextBtn = document.getElementById("saWorkloadNext");
        var pager = pagerEl && infoEl && prevBtn && nextBtn
            ? makePager(pagerEl, infoEl, prevBtn, nextBtn)
            : null;

        function apply(division) {
            chips.forEach(function (chip) {
                var on = chip.dataset.division === division;
                chip.classList.toggle("is-active", on);
                chip.setAttribute("aria-pressed", on ? "true" : "false");
            });

            var matching = rows.filter(function (row) {
                return !division || row.dataset.division === division;
            });
            rows.forEach(function (row) {
                if (matching.indexOf(row) === -1) row.hidden = true;
            });
            if (emptyRow) emptyRow.hidden = matching.length > 0;

            if (pager) {
                pager.setItems(matching);
            } else {
                matching.forEach(function (row) { row.hidden = false; });
            }
        }

        chips.forEach(function (chip) {
            chip.addEventListener("click", function () { apply(chip.dataset.division); });
        });

        apply(""); // initial render: all divisions, page 1
    });

    // Collapsed-by-default sections (Overdue Tasks, Employee Workload):
    // toggling just flips the body's `hidden` attribute, which doesn't touch
    // the row-count-based pagers above — they already ran at DOMContentLoaded
    // regardless of whether their section happened to be visible yet.
    document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll(".sa-collapse-toggle").forEach(function (btn) {
            var body = document.getElementById(btn.getAttribute("aria-controls"));
            if (!body) return;

            btn.addEventListener("click", function () {
                var expanded = btn.getAttribute("aria-expanded") === "true";
                btn.setAttribute("aria-expanded", expanded ? "false" : "true");
                body.hidden = expanded;
                var label = btn.getAttribute("data-collapse-label") || "section";
                btn.setAttribute("aria-label", (expanded ? "Expand " : "Collapse ") + label);
            });
        });
    });
})();
