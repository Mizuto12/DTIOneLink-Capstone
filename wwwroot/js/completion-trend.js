// Dashboard's Completion Trend widget (see DashboardController.GetCompletionTrend):
// loads the viewer's own weekly completion counts (own work / own division /
// office-wide, decided by the server from session role) and replaces the
// skeleton placeholder already in the page. Shared across the SuperAdmin,
// Admin, and Employee dashboards — each just needs the same container ids.
(function () {
  const chart = document.getElementById('completionTrendChart');
  const scopeEl = document.getElementById('completionTrendScope');
  if (!chart) return;

  function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value ?? '';
    return div.innerHTML;
  }

  function weekLabel(isoDateTime) {
    // WeekStart serializes as a full ISO datetime (e.g. "2026-08-25T00:00:00"),
    // not a bare date — no extra "T00:00:00" needed here.
    const d = new Date(isoDateTime);
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
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

    const max = Math.max(...weeks.map(w => w.count), 1);
    chart.innerHTML = weeks.map(w => {
      const heightPct = Math.round((w.count / max) * 100);
      const isZero = w.count === 0;
      return `
        <div class="ct-bar ${isZero ? 'ct-bar--zero' : ''}">
          <span class="ct-bar__count">${w.count}</span>
          <span class="ct-bar__fill" style="height:${isZero ? 3 : heightPct}%"></span>
          <span class="ct-bar__label">${escapeHtml(weekLabel(w.weekStart))}</span>
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
