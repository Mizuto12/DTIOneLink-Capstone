// ==========================================================
// DTI Laguna OneLink — Records Management JS
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        var form = document.getElementById("recordForm");
        if (!form) {
            return;
        }

        var button = form.querySelector(".rec-submit");
        var tbody = document.getElementById("recordRows");
        var emptyRow = document.getElementById("recordEmptyRow");
        var countLabel = document.getElementById("recordCount");

        var fields = [
            "code",
            "title",
            "medium",
            "location",
            "periodCovered",
            "filingSystem",
            "accessControl",
            "retentionPeriod"
        ];

        function value(name) {
            var field = form.elements[name];
            return field ? field.value.trim() : "";
        }

        function updateCount() {
            var total = tbody.querySelectorAll(".rec-row").length;
            countLabel.textContent = total === 0
                ? "No entries yet"
                : "Showing " + total + (total === 1 ? " entry" : " entries");
        }

        function addRow() {
            var row = document.createElement("tr");
            row.className = "rec-row";

            fields.forEach(function (name, index) {
                var cell = document.createElement("td");
                if (index === 1) {
                    cell.className = "rec-cell-strong";
                }
                cell.textContent = value(name) || "—";
                row.appendChild(cell);
            });

            emptyRow.hidden = true;
            tbody.insertBefore(row, tbody.firstChild);
            updateCount();
        }

        // ── Commit: saving -> success -> append row -> reset ──
        form.addEventListener("submit", function (e) {
            e.preventDefault();

            if (button.disabled) {
                return;
            }

            var originalContent = button.innerHTML;

            button.innerHTML =
                '<span class="material-symbols-outlined rec-spin">sync</span> Recording...';
            button.disabled = true;

            setTimeout(function () {
                addRow();

                button.innerHTML =
                    '<span class="material-symbols-outlined">check_circle</span> Success!';
                button.classList.add("is-success");

                setTimeout(function () {
                    button.innerHTML = originalContent;
                    button.classList.remove("is-success");
                    button.disabled = false;
                    form.reset();
                }, 2000);
            }, 1500);
        });

        updateCount();
    });
})();
