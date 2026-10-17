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

    // Each column's own card pager: a handful of cards per page (clicking
    // Next) instead of scrolling inside a small box. Reads its prev/next
    // buttons via [data-page] so the same markup works for any number of
    // columns without needing per-column ids.
    var CARD_PAGE_SIZE = 5;

    function makeCardPager(pagerEl) {
        var infoEl = pagerEl.querySelector(".sa-col__pager-info");
        var prevBtn = pagerEl.querySelector('[data-page="prev"]');
        var nextBtn = pagerEl.querySelector('[data-page="next"]');
        var page = 0;
        var items = [];

        function render() {
            var total = items.length;
            if (total <= CARD_PAGE_SIZE) {
                pagerEl.hidden = true;
                items.forEach(function (card) { card.hidden = false; });
                return;
            }

            var pageCount = Math.ceil(total / CARD_PAGE_SIZE);
            if (page >= pageCount) page = pageCount - 1;
            if (page < 0) page = 0;

            var start = page * CARD_PAGE_SIZE;
            var end = Math.min(start + CARD_PAGE_SIZE, total);
            items.forEach(function (card, i) { card.hidden = !(i >= start && i < end); });

            pagerEl.hidden = false;
            if (infoEl) infoEl.textContent = (start + 1) + "–" + end + " of " + total;
            if (prevBtn) prevBtn.disabled = page === 0;
            if (nextBtn) nextBtn.disabled = page === pageCount - 1;
        }

        if (prevBtn) prevBtn.addEventListener("click", function () { if (page > 0) { page--; render(); } });
        if (nextBtn) nextBtn.addEventListener("click", function () {
            var pageCount = Math.ceil(items.length / CARD_PAGE_SIZE);
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
        var columnPagers = columns.map(function (col) {
            var pagerEl = col.querySelector(".sa-col__pager");
            return pagerEl ? makeCardPager(pagerEl) : null;
        });

        function apply(division) {
            chips.forEach(function (chip) {
                var on = chip.dataset.division === division;
                chip.classList.toggle("is-active", on);
                chip.setAttribute("aria-pressed", on ? "true" : "false");
            });

            columns.forEach(function (col, idx) {
                var allCards = Array.from(col.querySelectorAll(".sa-kcard"));
                var matching = allCards.filter(function (card) {
                    return !division || card.dataset.division === division;
                });
                allCards.forEach(function (card) {
                    if (matching.indexOf(card) === -1) card.hidden = true;
                });

                var count = col.querySelector("[data-count]");
                if (count) count.textContent = String(matching.length);
                var empty = col.querySelector(".sa-col__empty");
                if (empty) empty.hidden = matching.length > 0;

                var pager = columnPagers[idx];
                if (pager) {
                    pager.setItems(matching);
                } else {
                    matching.forEach(function (card) { card.hidden = false; });
                }
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
        apply(saved && chips.some(function (c) { return c.dataset.division === saved; }) ? saved : "");
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
})();
