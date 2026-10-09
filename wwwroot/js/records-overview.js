// Dashboard's Records Overview card (see RecordsController.Overview):
// loads the viewer's own record counts and retention status, replacing the
// skeleton placeholders already in the page.
(function () {
  const container = document.getElementById('recordsOverviewCards');
  if (!container) return;

  function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value ?? '';
    return div.innerHTML;
  }

  function card(tone, icon, value, label) {
    return `
      <div class="ro-card ro-tone-${tone}">
        <span class="ro-card__icon material-symbols-outlined" aria-hidden="true">${icon}</span>
        <div>
          <p class="ro-card__value">${value}</p>
          <p class="ro-card__label">${escapeHtml(label)}</p>
        </div>
      </div>`;
  }

  fetch('/Records/Overview')
    .then(res => {
      if (!res.ok) throw new Error('Failed to load records overview');
      return res.json();
    })
    .then(data => {
      container.innerHTML =
        card('total', 'description', data.total, 'Total Records') +
        card('due-soon', 'event_busy', data.dueSoon, 'Due Soon') +
        card('overdue', 'warning', data.overdue, 'Needs Disposal Review') +
        card('saved', 'folder_open', data.savedMasterlists, 'Saved Masterlists');
    })
    .catch(() => {
      container.innerHTML = '<p class="records-overview__empty">Couldn\'t load your records overview.</p>';
    })
    .finally(() => {
      container.removeAttribute('aria-busy');
    });
})();
