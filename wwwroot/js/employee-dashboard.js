// ==========================================================
// DTI Laguna OneLink — Employee Dashboard
// Compact calendar preview → full calendar with selectable dates.
// Task data comes from #edTaskData (rendered by Views/Employee/Index.cshtml
// from the employee's real tasks). No data is invented here.
// ==========================================================
(function () {
    "use strict";

    var MONTHS = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];
    var WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    document.addEventListener("DOMContentLoaded", function () {
        var root = document.getElementById("employeeDashboard");
        if (!root) return;

        var workspace = document.getElementById("edWorkspace");
        var openBtn = document.getElementById("edCalOpen");
        var dialog = document.getElementById("edCalendar");
        var closeBtn = document.getElementById("edCalClose");
        var prevBtn = document.getElementById("edCalPrev");
        var nextBtn = document.getElementById("edCalNext");
        var todayBtn = document.getElementById("edCalToday");
        var titleEl = document.getElementById("edCalTitle");
        var monthEl = document.getElementById("edCalMonth");
        var yearEl = document.getElementById("edCalYear");
        var gridEl = document.getElementById("edCalGrid");
        var miniGrid = document.getElementById("edMiniGrid");
        var miniLabel = document.getElementById("edMiniLabel");
        var panelTitle = document.getElementById("edPanelTitle");
        var panelList = document.getElementById("edPanelList");

        var tasks = [];
        try {
            tasks = JSON.parse(document.getElementById("edTaskData").textContent || "[]");
        } catch (e) {
            tasks = [];
        }

        // Dates are "yyyy-MM-dd" strings (Philippine time, from the server).
        var todayKey = root.dataset.today || keyOf(new Date());
        var today = parseKey(todayKey);

        var byDue = groupBy(tasks, "due");
        var byAssigned = groupBy(tasks, "assigned");

        var viewYear = today.getFullYear();
        var viewMonth = today.getMonth();
        var selectedKey = todayKey;

        renderMini();

        // ── Open / close ───────────────────────────────────────
        openBtn.addEventListener("click", function () { openCalendar(); });

        closeBtn.addEventListener("click", closeCalendar);
        dialog.addEventListener("keydown", function (e) {
            if (e.key === "Escape") {
                e.preventDefault();
                closeCalendar();
            }
        });

        prevBtn.addEventListener("click", function () { shiftMonth(-1); });
        nextBtn.addEventListener("click", function () { shiftMonth(1); });
        todayBtn.addEventListener("click", function () {
            viewYear = today.getFullYear();
            viewMonth = today.getMonth();
            select(todayKey, true);
        });

        // Reopen after a live-refresh reload or a link back (#calendar=yyyy-MM-dd).
        var hashMatch = /^#calendar(?:=(\d{4}-\d{2}-\d{2}))?$/.exec(location.hash);
        if (hashMatch) {
            if (hashMatch[1]) {
                var d = parseKey(hashMatch[1]);
                if (!isNaN(d)) {
                    selectedKey = hashMatch[1];
                    viewYear = d.getFullYear();
                    viewMonth = d.getMonth();
                }
            }
            openCalendar(true);
        }

        function openCalendar(instant) {
            renderFull();
            renderPanel();
            workspace.hidden = true;
            dialog.show(); // in place, not modal: the page layout stays around it
            root.classList.toggle("is-instant", !!instant);
            root.classList.add("is-calendar");
            openBtn.setAttribute("aria-expanded", "true");
            setHash();
            // Keyboard users land on the selected day, ready for the arrow keys.
            var day = gridEl.querySelector('[data-date="' + selectedKey + '"]');
            (day || titleEl).focus({ preventScroll: true });
        }

        function closeCalendar() {
            dialog.close();
            root.classList.remove("is-calendar");
            workspace.hidden = false;
            openBtn.setAttribute("aria-expanded", "false");
            history.replaceState(null, "", location.pathname + location.search);
            openBtn.focus({ preventScroll: true });
        }

        function shiftMonth(delta) {
            viewMonth += delta;
            if (viewMonth < 0) { viewMonth = 11; viewYear--; }
            if (viewMonth > 11) { viewMonth = 0; viewYear++; }
            renderFull();
        }

        function select(key, focusDay) {
            selectedKey = key;
            renderFull();
            renderPanel();
            setHash();
            if (focusDay) {
                var btn = gridEl.querySelector('[data-date="' + key + '"]');
                if (btn) btn.focus();
            }
        }

        function setHash() {
            history.replaceState(null, "", location.pathname + location.search + "#calendar=" + selectedKey);
        }

        // ── Compact preview (dashboard) ────────────────────────
        function renderMini() {
            miniLabel.textContent = MONTHS[today.getMonth()] + " " + today.getFullYear();
            openBtn.setAttribute("aria-label", "Open calendar, " + MONTHS[today.getMonth()] + " " + today.getFullYear());
            miniGrid.textContent = "";
            eachCell(today.getFullYear(), today.getMonth(), function (key, day) {
                var cell = document.createElement("span");
                cell.className = "ed-mini-day";
                if (key) {
                    cell.textContent = day;
                    if (key === todayKey) cell.classList.add("is-today");
                    var due = byDue[key];
                    if (due && due.length) {
                        cell.classList.add("has-work");
                        if (due.some(function (t) { return t.overdue; })) cell.classList.add("has-overdue");
                    }
                } else {
                    cell.classList.add("is-blank");
                }
                miniGrid.appendChild(cell);
            });
        }

        // ── Full calendar ──────────────────────────────────────
        function renderFull() {
            monthEl.textContent = MONTHS[viewMonth];
            yearEl.textContent = viewYear;
            gridEl.textContent = "";

            eachCell(viewYear, viewMonth, function (key, day, weekday) {
                if (!key) {
                    var blank = document.createElement("span");
                    blank.className = "ed-day is-blank";
                    blank.setAttribute("aria-hidden", "true");
                    gridEl.appendChild(blank);
                    return;
                }

                var due = byDue[key] || [];
                var assigned = byAssigned[key] || [];

                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "ed-day";
                btn.dataset.date = key;
                if (weekday === 0) btn.classList.add("is-sunday");
                if (key === todayKey) btn.classList.add("is-today");
                if (key === selectedKey) btn.classList.add("is-selected");
                btn.setAttribute("aria-pressed", key === selectedKey ? "true" : "false");

                var num = document.createElement("span");
                num.className = "ed-day__num";
                num.textContent = day;
                btn.appendChild(num);

                if (due.length) {
                    var mark = document.createElement("span");
                    mark.className = "ed-day__mark" + (due.some(function (t) { return t.overdue; }) ? " is-overdue" : "");
                    mark.setAttribute("aria-hidden", "true");
                    btn.appendChild(mark);
                }

                var label = WEEKDAYS[weekday] + ", " + MONTHS[viewMonth] + " " + day + ", " + viewYear;
                if (key === todayKey) label += ", today";
                if (due.length) label += ", " + due.length + " due";
                if (assigned.length) label += ", " + assigned.length + " assigned";
                btn.setAttribute("aria-label", label);

                btn.addEventListener("click", function () { select(key, false); });
                btn.addEventListener("keydown", onDayKey);
                gridEl.appendChild(btn);
            });
        }

        // Arrow keys move between days (crossing into the next/previous month).
        function onDayKey(e) {
            var steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
            if (!steps) return;
            e.preventDefault();
            var d = parseKey(e.currentTarget.dataset.date);
            d.setDate(d.getDate() + steps);
            viewYear = d.getFullYear();
            viewMonth = d.getMonth();
            select(keyOf(d), true);
        }

        // ── Side panel: work for the selected date ─────────────
        function renderPanel() {
            var d = parseKey(selectedKey);
            panelTitle.textContent = WEEKDAYS[d.getDay()] + ", " + MONTHS[d.getMonth()] + " " + d.getDate() +
                (selectedKey === todayKey ? " (today)" : "");
            panelList.textContent = "";

            var due = (byDue[selectedKey] || []).slice().sort(byStatus);
            var dueIds = {};
            due.forEach(function (t) { dueIds[t.id] = true; });
            var assigned = (byAssigned[selectedKey] || []).filter(function (t) { return !dueIds[t.id]; });

            if (!due.length && !assigned.length) {
                var empty = document.createElement("div");
                empty.className = "ed-panel__empty";
                var icon = document.createElement("span");
                icon.className = "material-symbols-outlined";
                icon.setAttribute("aria-hidden", "true");
                icon.textContent = "calendar_today";
                var text = document.createElement("p");
                text.textContent = "Nothing due or assigned on this day.";
                empty.appendChild(icon);
                empty.appendChild(text);
                panelList.appendChild(empty);
                return;
            }

            if (due.length) panelList.appendChild(group("Due on this day", due, false));
            if (assigned.length) panelList.appendChild(group("Assigned on this day", assigned, true));
        }

        function group(heading, items, showDue) {
            var wrap = document.createElement("section");
            wrap.className = "ed-panel__group";
            var h = document.createElement("h4");
            h.className = "ed-panel__heading";
            h.textContent = heading + " (" + items.length + ")";
            wrap.appendChild(h);
            items.forEach(function (t) { wrap.appendChild(taskLink(t, showDue)); });
            return wrap;
        }

        function taskLink(t, showDue) {
            var a = document.createElement("a");
            a.className = "ed-dtask ed-tone-" + t.tone;
            a.href = t.url;

            var top = document.createElement("span");
            top.className = "ed-dtask__top";
            if (t.priority) {
                var prio = document.createElement("span");
                prio.className = "ed-prio ed-prio--" + (t.priorityKey || "medium");
                prio.textContent = t.priority;
                top.appendChild(prio);
            }
            var status = document.createElement("span");
            status.className = "ed-dtask__status";
            status.textContent = t.overdue ? "Overdue · " + t.status : t.status;
            top.appendChild(status);
            a.appendChild(top);

            var title = document.createElement("span");
            title.className = "ed-dtask__title";
            title.textContent = t.title;
            a.appendChild(title);

            if (t.subtask) {
                var sub = document.createElement("span");
                sub.className = "ed-dtask__sub";
                sub.textContent = "Subtask: " + t.subtask;
                a.appendChild(sub);
            }

            var meta = document.createElement("span");
            meta.className = "ed-dtask__meta";
            meta.textContent = (showDue ? "Due " + shortDate(t.due) + " · " : "") + t.progress + "% done";
            a.appendChild(meta);
            return a;
        }
    });

    // ── Helpers ─────────────────────────────────────────────────
    // Calls fn(key, day, weekday) for every cell of the month grid,
    // with key = null for the blank cells before the 1st and after the end.
    function eachCell(year, month, fn) {
        var first = new Date(year, month, 1).getDay();
        var days = new Date(year, month + 1, 0).getDate();
        var cells = Math.ceil((first + days) / 7) * 7;
        for (var i = 0; i < cells; i++) {
            var day = i - first + 1;
            if (day < 1 || day > days) {
                fn(null, null, i % 7);
            } else {
                fn(keyOf(new Date(year, month, day)), day, i % 7);
            }
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

    var STATUS_ORDER = { "To Do": 0, "In Progress": 1, "Returned": 2, "For Review": 3, "Completed": 4 };
    function byStatus(a, b) {
        if (a.overdue !== b.overdue) return a.overdue ? -1 : 1;
        return (STATUS_ORDER[a.status] || 0) - (STATUS_ORDER[b.status] || 0);
    }

    function keyOf(d) {
        return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate());
    }

    function parseKey(key) {
        var p = String(key).split("-");
        return new Date(+p[0], +p[1] - 1, +p[2]);
    }

    // "October 05" — same compact form as the board cards (DisplayHelpers.FormalMonthDay).
    function shortDate(key) {
        var d = parseKey(key);
        return MONTHS[d.getMonth()] + " " + pad(d.getDate());
    }

    function pad(n) { return n < 10 ? "0" + n : String(n); }
})();
