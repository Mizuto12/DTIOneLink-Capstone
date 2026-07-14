/* ==========================================================================
   AdminDashboard — Script
   Interactions for the content rendered inside <main class="main-content">.
   Topbar/sidebar behavior (profile dropdown, notif bell) stays in
   adminlayout.js — not duplicated here.
   ========================================================================== */

document.addEventListener("DOMContentLoaded", () => {
  initLedgerFilter();
  initCalendar();
  initTodoAdd();

  // TODO: fetch and render ledger rows into `.ledger-table tbody`
  // TODO: fetch and render performer cards into `.performers__grid`
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