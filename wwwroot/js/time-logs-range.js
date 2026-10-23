// ==========================================================
// DTI Laguna OneLink — Time Logs date range
// The From and To boxes stay ordinary date inputs (typeable, mm/dd/yyyy).
// The calendar button beside each opens a compact calendar styled like the
// Employee Dashboard's; picking a day fills that box. Typing and picking
// stay in sync, and the two boxes limit each other (To can't be before
// From; neither can be in the future), so the browser's own check stops
// Apply on an impossible range. Apply submits the same GET form as before —
// TimeLogsController does the filtering.
// ==========================================================
(function () {
    "use strict";

    var MONTHS = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];
    var WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    document.addEventListener("DOMContentLoaded", function () {
        var form = document.getElementById("tlFilter");
        var cal = document.getElementById("tlCalendar");
        if (!form || !cal) return;

        var fromInput = document.getElementById("filter-from");
        var toInput = document.getElementById("filter-to");
        var grid = cal.querySelector(".tl-cal__grid");
        var monthSel = cal.querySelector(".tl-cal__month");
        var yearSel = cal.querySelector(".tl-cal__year");
        var nextBtn = cal.querySelector('.tl-cal__nav[data-step="1"]');

        var todayKey = fromInput.dataset.today || keyOf(new Date());
        var today = parseKey(todayKey);
        var lastYear = today.getFullYear();
        var firstYear = lastYear - 5;

        var active = null;      // the .tl-date wrapper whose calendar is open
        var viewYear = lastYear;
        var viewMonth = today.getMonth();

        MONTHS.forEach(function (name, i) { monthSel.appendChild(new Option(name, String(i))); });
        for (var y = firstYear; y <= lastYear; y++) yearSel.appendChild(new Option(String(y), String(y)));

        form.querySelectorAll(".tl-date").forEach(function (wrap) {
            var btn = wrap.querySelector(".tl-date__btn");
            btn.addEventListener("click", function () {
                if (active === wrap) close(true); else open(wrap);
            });
        });

        // Typing keeps the limits (and an open calendar) in step.
        [fromInput, toInput].forEach(function (input) {
            input.addEventListener("input", sync);
            input.addEventListener("change", sync);
        });

        cal.querySelectorAll(".tl-cal__nav").forEach(function (btn) {
            btn.addEventListener("click", function () { shiftMonth(+btn.dataset.step); });
        });
        monthSel.addEventListener("change", function () { setView(viewYear, +monthSel.value); });
        yearSel.addEventListener("change", function () { setView(+yearSel.value, viewMonth); });
        cal.querySelector('[data-action="today"]').addEventListener("click", function () {
            setView(today.getFullYear(), today.getMonth());
            focusDay(todayKey);
        });
        cal.querySelector('[data-action="close"]').addEventListener("click", function () { close(true); });
        cal.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { e.preventDefault(); close(true); }
        });
        document.addEventListener("mousedown", function (e) {
            if (active && !cal.contains(e.target) && !active.contains(e.target)) close(false);
        });

        sync();

        function inputOf(wrap) { return wrap.dataset.role === "from" ? fromInput : toInput; }

        // To can't be before From, From can't be after To, nothing after today.
        function sync() {
            var from = fromInput.value, to = toInput.value;
            fromInput.max = to && to < todayKey ? to : todayKey;
            toInput.min = from || "";
            toInput.max = todayKey;
            toInput.setCustomValidity(from && to && to < from ? "The To date must be on or after the From date." : "");
            if (active) render();
        }

        function open(wrap) {
            if (active) close(false);
            active = wrap;
            wrap.appendChild(cal);
            var input = inputOf(wrap);
            var other = input === fromInput ? toInput : fromInput;
            var anchor = parseKey(input.value || other.value || todayKey);
            if (anchor > today) anchor = today;
            viewYear = Math.max(firstYear, anchor.getFullYear());
            viewMonth = anchor.getMonth();
            cal.hidden = false;
            cal.setAttribute("aria-label", wrap.dataset.role === "from" ? "Choose the From date" : "Choose the To date");
            wrap.querySelector(".tl-date__btn").setAttribute("aria-expanded", "true");
            render();
            focusDay(input.value || todayKey);
        }

        function close(returnFocus) {
            if (!active) return;
            var btn = active.querySelector(".tl-date__btn");
            btn.setAttribute("aria-expanded", "false");
            cal.hidden = true;
            active = null;
            if (returnFocus) btn.focus();
        }

        function shiftMonth(delta) {
            var d = new Date(viewYear, viewMonth + delta, 1);
            setView(d.getFullYear(), d.getMonth());
        }

        function setView(year, month) {
            if (year < firstYear || year > lastYear) return;
            if (year === lastYear && month > today.getMonth()) month = today.getMonth();
            viewYear = year;
            viewMonth = month;
            render();
        }

        function pick(key) {
            var input = inputOf(active);
            input.value = key;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            close(false);
            input.focus();
        }

        function render() {
            if (!active) return;
            var input = inputOf(active);
            var isFrom = input === fromInput;
            var from = fromInput.value, to = toInput.value;
            var lo = input.min || "", hi = input.max || todayKey;

            monthSel.value = String(viewMonth);
            yearSel.value = String(viewYear);
            nextBtn.disabled = viewYear === lastYear && viewMonth >= today.getMonth();

            grid.textContent = "";
            eachCell(viewYear, viewMonth, function (key, day, weekday) {
                if (!key) {
                    var blank = document.createElement("span");
                    blank.className = "tl-day is-blank";
                    blank.setAttribute("aria-hidden", "true");
                    grid.appendChild(blank);
                    return;
                }
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "tl-day";
                btn.dataset.date = key;
                btn.tabIndex = -1;
                btn.textContent = day;

                var selected = key === input.value;
                if (key === todayKey) btn.classList.add("is-today");
                if (selected) btn.classList.add("is-selected");
                // The other end of the range, and the days between, for context.
                if (key === (isFrom ? to : from) && !selected) btn.classList.add("is-other-end");
                if (from && to && key > from && key < to) btn.classList.add("is-in-range");
                btn.setAttribute("aria-pressed", selected ? "true" : "false");

                var label = WEEKDAYS[weekday] + ", " + MONTHS[viewMonth] + " " + day + ", " + viewYear;
                if (key === todayKey) label += ", today";
                if ((lo && key < lo) || key > hi) {
                    btn.disabled = true;
                    label += ", unavailable";
                }
                btn.setAttribute("aria-label", label);

                btn.addEventListener("click", function () { pick(key); });
                btn.addEventListener("keydown", onDayKey);
                grid.appendChild(btn);
            });

            var stop = grid.querySelector(".tl-day.is-selected:not(:disabled)") ||
                grid.querySelector(".tl-day.is-today:not(:disabled)") ||
                grid.querySelector(".tl-day:not(.is-blank):not(:disabled)");
            if (stop) stop.tabIndex = 0;
        }

        // Arrow keys move between days, crossing into other months.
        function onDayKey(e) {
            var steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
            if (!steps) return;
            e.preventDefault();
            var d = parseKey(e.currentTarget.dataset.date);
            d.setDate(d.getDate() + steps);
            if (d.getFullYear() < firstYear || keyOf(d) > todayKey) return;
            setView(d.getFullYear(), d.getMonth());
            focusDay(keyOf(d));
        }

        function focusDay(key) {
            var btn = grid.querySelector('[data-date="' + key + '"]:not(:disabled)');
            if (!btn) return;
            grid.querySelectorAll(".tl-day[tabindex='0']").forEach(function (b) { b.tabIndex = -1; });
            btn.tabIndex = 0;
            btn.focus({ preventScroll: true });
        }
    });

    // ── Helpers (same as the Dashboard calendar's) ──────────────
    function eachCell(year, month, fn) {
        var first = new Date(year, month, 1).getDay();
        var days = new Date(year, month + 1, 0).getDate();
        var cells = Math.ceil((first + days) / 7) * 7;
        for (var i = 0; i < cells; i++) {
            var day = i - first + 1;
            if (day < 1 || day > days) fn(null, null, i % 7);
            else fn(keyOf(new Date(year, month, day)), day, i % 7);
        }
    }

    function keyOf(d) { return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }

    function parseKey(key) {
        var p = String(key).split("-");
        return new Date(+p[0], +p[1] - 1, +p[2]);
    }

    function pad(n) { return n < 10 ? "0" + n : String(n); }
})();
