// ==========================================================
// DTI Laguna OneLink — Employee Dashboard calendar
// A compact month calendar beside My Tasks. A blue dot marks each day that
// has a task due. The list under the calendar shows the selected day's To Do
// tasks due that day (today when the page opens). The To Do / In Progress
// frames always list every task — the calendar doesn't change them.
// "Expand" shows a larger calendar with the same day list beside it, in place
// of the dashboard; both views share the month and the selected date.
// Task data comes from #edTaskData (Views/Employee/Index.cshtml: the
// employee's own active tasks). Nothing is invented or changed here.
// ==========================================================
(function () {
    "use strict";

    var MONTHS = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];
    var WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    document.addEventListener("DOMContentLoaded", function () {
        var root = document.getElementById("employeeDashboard");
        if (!root) return;

        var layout = document.getElementById("edWorkspace");
        var expanded = document.getElementById("edCalendar");
        var expandBtn = document.getElementById("edCalExpand");
        var closeBtn = document.getElementById("edCalClose");
        var calendars = Array.prototype.slice.call(root.querySelectorAll(".ed-cal"));
        var panels = Array.prototype.slice.call(root.querySelectorAll(".ed-day-panel"));

        var tasks = [];
        try {
            tasks = JSON.parse(document.getElementById("edTaskData").textContent || "[]");
        } catch (e) {
            tasks = [];
        }

        // Dates are "yyyy-MM-dd" strings in Philippine time, from the server.
        var todayKey = root.dataset.today || keyOf(new Date());
        var today = parseKey(todayKey);
        var byDue = groupBy(tasks, "due");

        var viewYear = today.getFullYear();
        var viewMonth = today.getMonth();
        var selectedKey = todayKey;

        // Year choices: a year before the earliest due date to two years past the latest.
        var years = tasks.reduce(function (acc, t) {
            if (t.due) acc.push(+t.due.slice(0, 4));
            return acc;
        }, [today.getFullYear()]);
        var firstYear = Math.min.apply(null, years) - 1;
        var lastYear = Math.max.apply(null, years) + 2;

        calendars.forEach(setUpCalendar);
        render();

        expandBtn.addEventListener("click", function () { openExpanded(false); });
        closeBtn.addEventListener("click", closeExpanded);
        expanded.addEventListener("keydown", function (e) {
            if (e.key === "Escape") { e.preventDefault(); closeExpanded(); }
        });

        // Reopen the expanded view after a live-refresh reload (#calendar=yyyy-MM-dd).
        var hashMatch = /^#calendar(?:=(\d{4}-\d{2}-\d{2}))?$/.exec(location.hash);
        if (hashMatch) {
            if (hashMatch[1] && !isNaN(parseKey(hashMatch[1]))) selectedKey = hashMatch[1];
            openExpanded(true);
        }

        function setUpCalendar(cal) {
            var monthSel = cal.querySelector(".ed-cal__month");
            var yearSel = cal.querySelector(".ed-cal__year");
            MONTHS.forEach(function (name, i) { monthSel.appendChild(new Option(name, String(i))); });
            for (var y = firstYear; y <= lastYear; y++) yearSel.appendChild(new Option(String(y), String(y)));

            cal.querySelectorAll(".ed-cal__nav").forEach(function (btn) {
                btn.addEventListener("click", function () { shiftMonth(+btn.dataset.step); });
            });
            monthSel.addEventListener("change", function () { viewMonth = +monthSel.value; render(); });
            yearSel.addEventListener("change", function () { viewYear = +yearSel.value; render(); });
            cal.querySelector(".ed-cal__today").addEventListener("click", function () {
                viewYear = today.getFullYear();
                viewMonth = today.getMonth();
                select(todayKey, cal);
            });
        }

        function shiftMonth(delta) {
            var d = new Date(viewYear, viewMonth + delta, 1);
            if (d.getFullYear() < firstYear || d.getFullYear() > lastYear) return;
            viewYear = d.getFullYear();
            viewMonth = d.getMonth();
            render();
        }

        function select(key, focusIn) {
            selectedKey = key;
            render();
            if (!expanded.hidden) setHash();
            if (focusIn) {
                var btn = focusIn.querySelector('[data-date="' + key + '"]');
                if (btn) btn.focus();
            }
        }

        function render() {
            calendars.forEach(renderCalendar);
            panels.forEach(renderPanel);
        }

        // ── Month grid: a blue dot on each day with something due ──
        function renderCalendar(cal) {
            cal.querySelector(".ed-cal__month").value = String(viewMonth);
            cal.querySelector(".ed-cal__year").value = String(viewYear);
            var grid = cal.querySelector(".ed-cal__grid");
            grid.textContent = "";

            eachCell(viewYear, viewMonth, function (key, day, weekday) {
                if (!key) {
                    var blank = document.createElement("span");
                    blank.className = "ed-day is-blank";
                    blank.setAttribute("aria-hidden", "true");
                    grid.appendChild(blank);
                    return;
                }
                var due = byDue[key] || [];

                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "ed-day";
                btn.dataset.date = key;
                if (key === todayKey) btn.classList.add("is-today");
                if (key === selectedKey) btn.classList.add("is-selected");
                btn.setAttribute("aria-pressed", key === selectedKey ? "true" : "false");

                var num = document.createElement("span");
                num.className = "ed-day__num";
                num.textContent = day;
                btn.appendChild(num);

                var dot = document.createElement("span");
                dot.className = "ed-day__dot" + (due.length ? " is-due" : "");
                dot.setAttribute("aria-hidden", "true");
                btn.appendChild(dot);

                var label = WEEKDAYS[weekday] + ", " + MONTHS[viewMonth] + " " + day + ", " + viewYear;
                if (key === todayKey) label += ", today";
                if (due.length) label += ", " + due.length + (due.length === 1 ? " task" : " tasks") + " due";
                btn.setAttribute("aria-label", label);
                btn.title = label;

                btn.addEventListener("click", function () { select(key, null); });
                btn.addEventListener("keydown", function (e) { onDayKey(e, cal); });
                grid.appendChild(btn);
            });
        }

        // Arrow keys move between days, crossing into the next or previous month.
        function onDayKey(e, cal) {
            var steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
            if (!steps) return;
            e.preventDefault();
            var d = parseKey(e.currentTarget.dataset.date);
            d.setDate(d.getDate() + steps);
            if (d.getFullYear() < firstYear || d.getFullYear() > lastYear) return;
            viewYear = d.getFullYear();
            viewMonth = d.getMonth();
            select(keyOf(d), cal);
        }

        // ── The selected day's To Do tasks (due that day) ───────
        function renderPanel(panel) {
            var d = parseKey(selectedKey);
            panel.textContent = "";

            var title = document.createElement("p");
            title.className = "ed-day-panel__date";
            title.textContent = WEEKDAYS[d.getDay()] + ", " + MONTHS[d.getMonth()] + " " + pad(d.getDate()) + ", " + d.getFullYear() +
                (selectedKey === todayKey ? " (Today)" : "");
            panel.appendChild(title);

            var todo = (byDue[selectedKey] || []).filter(function (t) { return t.status === "To Do"; });
            if (!todo.length) {
                var empty = document.createElement("p");
                empty.className = "ed-day-panel__empty";
                empty.textContent = "No to-do tasks due on this date.";
                panel.appendChild(empty);
                return;
            }

            var list = document.createElement("ul");
            list.className = "ed-day-list";
            todo.forEach(function (t) { list.appendChild(dayItem(t)); });
            panel.appendChild(list);
        }

        function dayItem(t) {
            var li = document.createElement("li");
            var a = document.createElement("a");
            a.className = "ed-day-item";
            a.href = t.url;

            var name = document.createElement("span");
            name.className = "ed-day-item__name";
            if (t.overdue) {
                var dot = document.createElement("span");
                dot.className = "ed-overdue-dot";
                dot.setAttribute("role", "img");
                dot.setAttribute("aria-label", "Overdue task");
                dot.title = "Overdue task";
                name.appendChild(dot);
            }
            name.appendChild(document.createTextNode(t.title));
            a.appendChild(name);

            if (t.subtask) {
                var sub = document.createElement("span");
                sub.className = "ed-day-item__sub";
                sub.textContent = "Subtask: " + t.subtask;
                a.appendChild(sub);
            }

            if (t.priority) {
                var meta = document.createElement("span");
                meta.className = "ed-day-item__meta";
                meta.textContent = t.priority + " priority";
                a.appendChild(meta);
            }

            li.appendChild(a);
            return li;
        }

        // ── Expanded view ──────────────────────────────────────
        function openExpanded(instant) {
            var d = parseKey(selectedKey);
            viewYear = d.getFullYear();
            viewMonth = d.getMonth();
            render();
            layout.hidden = true;
            expanded.hidden = false;
            root.classList.add("is-expanded");
            root.classList.toggle("is-instant", !!instant);
            expandBtn.setAttribute("aria-expanded", "true");
            setHash();
            var day = expanded.querySelector('[data-date="' + selectedKey + '"]');
            if (day) day.focus({ preventScroll: true });
        }

        function closeExpanded() {
            expanded.hidden = true;
            layout.hidden = false;
            root.classList.remove("is-expanded");
            expandBtn.setAttribute("aria-expanded", "false");
            history.replaceState(null, "", location.pathname + location.search);
            expandBtn.focus({ preventScroll: true });
        }

        function setHash() {
            history.replaceState(null, "", location.pathname + location.search + "#calendar=" + selectedKey);
        }
    });

    // ── Helpers ─────────────────────────────────────────────────
    // Calls fn(key, day, weekday) for every cell of the month grid, with
    // key = null for the blank cells before the 1st and after the last day.
    function eachCell(year, month, fn) {
        var first = new Date(year, month, 1).getDay();
        var days = new Date(year, month + 1, 0).getDate(); // handles leap years
        var cells = Math.ceil((first + days) / 7) * 7;
        for (var i = 0; i < cells; i++) {
            var day = i - first + 1;
            if (day < 1 || day > days) fn(null, null, i % 7);
            else fn(keyOf(new Date(year, month, day)), day, i % 7);
        }
    }

    function groupBy(list, field) {
        var map = {};
        list.forEach(function (t) {
            if (!t[field]) return;
            (map[t[field]] = map[t[field]] || []).push(t);
        });
        return map;
    }

    function keyOf(d) { return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }

    function parseKey(key) {
        var p = String(key).split("-");
        return new Date(+p[0], +p[1] - 1, +p[2]);
    }

    function pad(n) { return n < 10 ? "0" + n : String(n); }
})();
