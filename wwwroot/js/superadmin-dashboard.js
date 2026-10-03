// ==========================================================
// DTI Laguna OneLink — Super Admin Dashboard
// Division chips on the Task Board: show one division's cards
// (or all of them) and keep each column's count in step.
// ==========================================================
(function () {
    "use strict";

    var STORAGE_KEY = "saBoardDivision";

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

        chips.forEach(function (chip) {
            chip.addEventListener("click", function () { apply(chip.dataset.division); });
        });

        var saved = "";
        try { saved = sessionStorage.getItem(STORAGE_KEY) || ""; } catch (e) { /* storage blocked */ }
        if (saved && chips.some(function (c) { return c.dataset.division === saved; })) {
            apply(saved);
        }
    });
})();
