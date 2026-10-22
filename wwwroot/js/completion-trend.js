// Dashboard's Completion Trend widget (see DashboardController.GetCompletionTrend):
// loads the viewer's own weekly completion counts (own work / own division /
// office-wide, decided by the server from session role) and replaces the
// skeleton placeholder already in the page. Shared across the SuperAdmin,
// Admin, and Employee dashboards — each just needs the same container ids.
(function () {
  const chart = document.getElementById('completionTrendChart');
  const scopeEl = document.getElementById('completionTrendScope');
  const subEl = document.getElementById('completionTrendSub');
  if (!chart) return;

  function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value ?? '';
    return div.innerHTML;
  }

  const MONTH_ABBR = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

  function monthDay(isoDateTime) {
    const d = new Date(isoDateTime);
    return `${MONTH_ABBR[d.getMonth()]} ${d.getDate()}`;
  }

  // Per-bar labels at rail width have roughly 28px to work with — even the
  // shortest month+day combo ("Oct 5") overflows that and collides with its
  // neighbor. A bare day number ("5", "12", "19") is the one thing that
  // always fits, so the month only appears once, spelled out in the
  // subtitle's date range (see render()) rather than repeated on every bar.
  function weekLabels(weeks) {
    return weeks.map(w => `${new Date(w.weekStart).getDate()}`);
  }

  function render(data) {
    if (scopeEl) {
      scopeEl.textContent = data.scopeLabel || '';
    }

    const weeks = data.weeks || [];
    if (weeks.length === 0 || weeks.every(w => w.count === 0)) {
      chart.innerHTML = '<p class="ct-empty">No completed tasks in the last 8 weeks yet.</p>';
      return;
    }

    // One number up front, so the headline doesn't require reading all 8
    // bars, plus the actual date range (bar labels below are bare day
    // numbers, so this is the only place the month shows at all).
    const total = weeks.reduce((sum, w) => sum + w.count, 0);
    if (subEl) {
      const range = weeks.length > 1
        ? `${monthDay(weeks[0].weekStart)}–${monthDay(weeks[weeks.length - 1].weekStart)}`
        : monthDay(weeks[0].weekStart);
      subEl.textContent = `${total} completed · ${range}`;
    }

    const max = Math.max(...weeks.map(w => w.count), 1);
    const labels = weekLabels(weeks);
    chart.innerHTML = weeks.map((w, i) => {
      const heightPct = Math.round((w.count / max) * 100);
      const isZero = w.count === 0;
      const isPeak = w.count > 0 && w.count === max;
      const barClass = ['ct-bar', isZero ? 'ct-bar--zero' : '', isPeak ? 'ct-bar--peak' : ''].filter(Boolean).join(' ');
      return `
        <div class="${barClass}">
          <span class="ct-bar__count">${w.count}</span>
          <span class="ct-bar__fill" style="height:${isZero ? 3 : heightPct}%"></span>
          <span class="ct-bar__label">${escapeHtml(labels[i])}</span>
        </div>`;
    }).join('');
  }

  fetch('/Dashboard/GetCompletionTrend')
    .then(res => {
      if (!res.ok) throw new Error('Failed to load completion trend');
      return res.json();
    })
    .then(render)
    .catch(() => {
      chart.innerHTML = '<p class="ct-empty">Couldn\'t load the completion trend.</p>';
    })
    .finally(() => {
      chart.classList.remove('ct-skeleton');
      chart.removeAttribute('aria-busy');
    });
})();
