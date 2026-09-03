/* ==========================================================================
   AdminDashboard — Script
   Interactions for the content rendered inside <main class="main-content">.
   Topbar/sidebar behavior (profile dropdown, notif bell) stays in
   adminlayout.js — not duplicated here.
   ========================================================================== */

document.addEventListener("DOMContentLoaded", () => {
  initLedgerFilter();
  initLedgerViewToggle();
  initCalendar();
  initTodoAdd();
  initTodoSummaryToggle();
  initTaskNavigation();

  // TODO: fetch and render announcements into the announcements widget
});

/**
 * Placeholder hook for the ledger's Filter button.
 */
function initLedgerFilter() {
  const filterBtn = document.querySelector(".ledger__header .btn-pill");
  if (!filterBtn) return;

  filterBtn.addEventListener("click", () => {
    // TODO: open filter menu / apply filters to the ledger table
    console.log("Filter clicked — hook up filter UI here.");
  });
}

/**
 * Switches the Global Task Ledger between the Gantt view and the Kanban
 * view. Mirrors the markup rendered by _DashboardContent.cshtml:
 *   #btnGanttView / #btnKanbanView  — the pill toggle buttons
 *   #ganttView    / #kanbanView     — the two view containers
 */
function initLedgerViewToggle() {
  const ganttBtn = document.getElementById("btnGanttView");
  const kanbanBtn = document.getElementById("btnKanbanView");
  const ganttView = document.getElementById("ganttView");
  const kanbanView = document.getElementById("kanbanView");

  if (!ganttBtn || !kanbanBtn || !ganttView || !kanbanView) return;

  function showGantt() {
    ganttView.classList.remove("is-hidden");
    kanbanView.classList.add("is-hidden");
    ganttBtn.classList.add("is-active");
    ganttBtn.setAttribute("aria-selected", "true");
    kanbanBtn.classList.remove("is-active");
    kanbanBtn.setAttribute("aria-selected", "false");
  }

  function showKanban() {
    kanbanView.classList.remove("is-hidden");
    ganttView.classList.add("is-hidden");
    kanbanBtn.classList.add("is-active");
    kanbanBtn.setAttribute("aria-selected", "true");
    ganttBtn.classList.remove("is-active");
    ganttBtn.setAttribute("aria-selected", "false");
  }

  ganttBtn.addEventListener("click", showGantt);
  kanbanBtn.addEventListener("click", showKanban);
}

/**
 * Renders a real, navigable month-view calendar into #calendarDays,
 * driven by the actual current date rather than hardcoded values.
 */
function initCalendar() {
  const labelEl = document.getElementById("calendarLabel");
  const daysEl = document.getElementById("calendarDays");
  const prevBtn = document.getElementById("calendarPrevBtn");
  const nextBtn = document.getElementById("calendarNextBtn");

  if (!labelEl || !daysEl) return;

  const monthNames = [
    "Jan", "Feb", "Mar", "Apr", "May", "Jun",
    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
  ];

  const today = new Date();
  let viewYear = today.getFullYear();
  let viewMonth = today.getMonth(); // 0-indexed

  function renderCalendar(year, month) {
    labelEl.textContent = `${monthNames[month]} ${year}`;
    daysEl.innerHTML = "";

    const startWeekday = new Date(year, month, 1).getDay(); // 0 = Sun
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const daysInPrevMonth = new Date(year, month, 0).getDate();
    const totalCells = Math.ceil((startWeekday + daysInMonth) / 7) * 7;

    const isRealCurrentMonth =
      year === today.getFullYear() && month === today.getMonth();

    const fragment = document.createDocumentFragment();

    for (let i = 0; i < totalCells; i++) {
      const dayNumber = i - startWeekday + 1;
      const span = document.createElement("span");

      if (dayNumber < 1) {
        span.textContent = daysInPrevMonth + dayNumber;
        span.classList.add("calendar-day--muted");
      } else if (dayNumber > daysInMonth) {
        span.textContent = dayNumber - daysInMonth;
        span.classList.add("calendar-day--muted");
      } else {
        span.textContent = dayNumber;
        if (isRealCurrentMonth && dayNumber === today.getDate()) {
          span.classList.add("calendar-day--today");
        } else {
          span.classList.add("calendar-day--current-month");
          span.addEventListener("click", () => {
            // TODO: handle day selection (e.g. filter ledger by due date)
            console.log(`Day clicked: ${year}-${month + 1}-${dayNumber}`);
          });
        }
      }

      fragment.appendChild(span);
    }

    daysEl.appendChild(fragment);
  }

  renderCalendar(viewYear, viewMonth);

  if (prevBtn) {
    prevBtn.addEventListener("click", () => {
      viewMonth -= 1;
      if (viewMonth < 0) {
        viewMonth = 11;
        viewYear -= 1;
      }
      renderCalendar(viewYear, viewMonth);
    });
  }

  if (nextBtn) {
    nextBtn.addEventListener("click", () => {
      viewMonth += 1;
      if (viewMonth > 11) {
        viewMonth = 0;
        viewYear += 1;
      }
      renderCalendar(viewYear, viewMonth);
    });
  }
}

/**
 * Placeholder hook for adding a to-do item.
 */
function initTodoAdd() {
  const addBtn = document.querySelector(".widget__add-btn");
  if (!addBtn) return;

  addBtn.addEventListener("click", () => {
    // TODO: open "add to-do" input / modal and append to `.todo-list`
    console.log("Add to-do clicked.");
  });
}

/**
 * Toggles the collapsible "N tasks due" summary in the To-do widget.
 */
function initTodoSummaryToggle() {
  const toggle = document.getElementById("todoSummaryToggle");
  const chevronBtn = document.getElementById("todoSummaryChevron");
  const list = document.getElementById("todoList");

  if (!list || (!toggle && !chevronBtn)) return;

  function toggleList() {
    const isOpen = list.style.display !== "none";
    list.style.display = isOpen ? "none" : "block";
    if (chevronBtn) chevronBtn.classList.toggle("open", !isOpen);
  }

  if (toggle) toggle.addEventListener("click", toggleList);
  if (chevronBtn) chevronBtn.addEventListener("click", toggleList);
}

/**
 * Clicking (or pressing Enter/Space on) a Gantt row or Kanban card opens
 * that task's detail view. Clicks on the Kanban card's "⋯" menu button
 * are excluded so they don't also trigger navigation.
 *
 * Sets cursor:pointer inline rather than in admindashboard.css, since
 * that stylesheet wasn't provided here — move these two rules into
 * admindashboard.css (.gantt-row, .kanban-card { cursor: pointer; })
 * once you've confirmed it there instead.
 */
function initTaskNavigation() {
  const clickableRows = document.querySelectorAll(
    ".gantt-row[data-href], .kanban-card[data-href]"
  );

  clickableRows.forEach((el) => {
    el.style.cursor = "pointer";

    el.addEventListener("click", (event) => {
      if (event.target.closest(".kanban-card__more-btn")) return;
      const href = el.getAttribute("data-href");
      if (href) window.location.href = href;
    });

    el.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" && event.key !== " ") return;
      if (event.target.closest(".kanban-card__more-btn")) return;
      event.preventDefault();
      const href = el.getAttribute("data-href");
      if (href) window.location.href = href;
    });
  });
}