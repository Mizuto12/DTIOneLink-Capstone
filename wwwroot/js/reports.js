(function () {
  const PAGE_SIZE = 4; // pagination only kicks in once there are more than 4 (filtered) reports

  // ---------- Data layer ----------
  // Starts empty. Each report: { id, title, owner, tag, badge, icon, tone, time, category }
  // "category" must match one of the filter option values in the dropdown.
  let reports = [];

  let currentPage = 1;
  let currentFilter = 'All Categories';
  let searchTerm = '';

  const reportListEl = document.getElementById('report-list');
  const paginationStatusEl = document.getElementById('pagination-status');
  const paginationControlsEl = document.getElementById('pagination-controls');
  const currentFilterLabel = document.getElementById('currentFilter');
  const filterBtn = document.getElementById('filterBtn');
  const filterDropdown = document.getElementById('filterDropdown');
  const searchInput = document.getElementById('report-search');

  // The page starts with placeholder rows; they stay until the data arrives,
  // so slow wifi never shows "No reports yet" for reports still loading.
  const skeletonHtml = reportListEl.innerHTML;
  let loaded = false;

  // ---------- Filtering ----------
  function getFilteredReports() {
    return reports.filter(r => {
      const matchesCategory = currentFilter === 'All Categories' || r.category === currentFilter;
      const term = searchTerm.toLowerCase();
      const matchesSearch = term === '' ||
        [r.title, r.owner, r.id, r.tag].some(v => (v || '').toLowerCase().includes(term));
      return matchesCategory && matchesSearch;
    });
  }

  // ---------- Rendering ----------
  function renderReportItem(r) {
    const el = document.createElement('div');
    el.className = 'report-item';
    el.innerHTML = `
      <div class="item-icon tone-${r.tone}">
        <span class="material-symbols-outlined filled-icon">${r.icon}</span>
      </div>
      <div class="item-info">
        <h3 class="item-title">${escapeHtml(r.title)}</h3>
        <div class="item-meta">
          <span class="meta-owner">${escapeHtml(r.owner)}</span>
          <span class="meta-dot"></span>
          ${r.badge
            ? `<span class="meta-badge">${escapeHtml(r.badge)}</span>`
            : `<span class="meta-tag">${escapeHtml(r.tag)}</span>`}
        </div>
      </div>
      <div class="item-right">
        <span class="item-time">${escapeHtml(r.time)}</span>
        <span class="item-id">ID: #${escapeHtml(r.id)}</span>
      </div>
      <button class="item-menu-btn" type="button" aria-label="More options">
        <span class="material-symbols-outlined">more_vert</span>
      </button>
    `;
    return el;
  }

  function renderEmptyState(message) {
    const el = document.createElement('div');
    el.className = 'empty-state';
    el.innerHTML = `
      <span class="material-symbols-outlined">inbox</span>
      <span class="empty-state-title">${escapeHtml(message)}</span>
    `;
    return el;
  }

  function renderPageNumbers(totalPages) {
    paginationControlsEl.innerHTML = '';

    const prevBtn = document.createElement('button');
    prevBtn.type = 'button';
    prevBtn.className = 'page-nav';
    prevBtn.setAttribute('aria-label', 'Previous page');
    prevBtn.innerHTML = '<span class="material-symbols-outlined">chevron_left</span>';
    prevBtn.disabled = currentPage === 1;
    prevBtn.addEventListener('click', () => goToPage(currentPage - 1));
    paginationControlsEl.appendChild(prevBtn);

    const current = document.createElement('span');
    current.className = 'page-current';
    current.textContent = currentPage;
    paginationControlsEl.appendChild(current);

    const nextBtn = document.createElement('button');
    nextBtn.type = 'button';
    nextBtn.className = 'page-nav';
    nextBtn.setAttribute('aria-label', 'Next page');
    nextBtn.innerHTML = '<span class="material-symbols-outlined">chevron_right</span>';
    nextBtn.disabled = currentPage === totalPages;
    nextBtn.addEventListener('click', () => goToPage(currentPage + 1));
    paginationControlsEl.appendChild(nextBtn);
  }

  function render() {
    if (!loaded) return; // keep the placeholders (or the error) on screen
    const filtered = getFilteredReports();
    const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    if (currentPage > totalPages) currentPage = totalPages;

    reportListEl.innerHTML = '';

    if (filtered.length === 0) {
      const periodChosen = document.getElementById('period-select').value !== 'all';
      const message = reports.length === 0
        ? (periodChosen ? 'No activity in this period.' : 'No reports yet.')
        : 'No reports match your search or filter.';
      reportListEl.appendChild(renderEmptyState(message));
    } else {
      const start = (currentPage - 1) * PAGE_SIZE;
      const pageItems = filtered.slice(start, start + PAGE_SIZE);
      pageItems.forEach(r => reportListEl.appendChild(renderReportItem(r)));
    }

    // Pagination bar is always visible; only the enabled/disabled state of the
    // arrows changes based on whether there's a previous/next page.
    renderPageNumbers(totalPages);

    const shownStart = filtered.length === 0 ? 0 : (currentPage - 1) * PAGE_SIZE + 1;
    const shownEnd = Math.min(currentPage * PAGE_SIZE, filtered.length);
    paginationStatusEl.textContent = filtered.length === 0
      ? 'Showing 0 of 0 reports'
      : `Showing ${shownStart}-${shownEnd} of ${filtered.length} reports`;
  }

  function goToPage(num) {
    const filtered = getFilteredReports();
    const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    currentPage = Math.min(Math.max(1, num), totalPages);
    render();
  }

  function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value ?? '';
    return div.innerHTML;
  }

  // ---------- Filter dropdown ----------
  filterBtn.addEventListener('click', () => {
    filterDropdown.classList.toggle('open');
  });

  filterDropdown.querySelectorAll('.filter-option').forEach(btn => {
    btn.addEventListener('click', () => {
      currentFilter = btn.dataset.value;
      currentFilterLabel.textContent = currentFilter;
      filterDropdown.querySelectorAll('.filter-option').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      filterDropdown.classList.remove('open');
      currentPage = 1;
      render();
    });
  });

  window.addEventListener('click', (e) => {
    if (!filterBtn.contains(e.target) && !filterDropdown.contains(e.target)) {
      filterDropdown.classList.remove('open');
    }
  });

  // ---------- Search ----------
  searchInput.addEventListener('input', (e) => {
    searchTerm = e.target.value;
    currentPage = 1;
    render();
  });

  // ---------- Public API for wiring real data in later ----------
  // Example: ReportsPage.addReport({ id: '45821-B', title: 'Business Registration Report',
  //   owner: 'Lao, Chandrei Emerson V.', tag: 'Processing', badge: null,
  //   icon: 'schedule', tone: 'primary', time: '2 hours ago', category: 'Task History' });
  window.ReportsPage = {
    addReport(report) {
      reports.push(report);
      render();
    },
    setReports(newReports) {
      reports = newReports;
      loaded = true;
      currentPage = 1;
      render();
    },
    getReports() {
      return reports.slice();
    }
  };

  // ---------- Load real data from the server ----------
  // ReportsController.Data returns only what this user is allowed to see.
  function renderLoadError() {
    reportListEl.innerHTML = `
      <div class="load-error" role="alert">
        <span class="material-symbols-outlined">error</span>
        <p>Couldn't load reports. Check your connection.</p>
        <button type="button" class="load-error-retry">Try again</button>
      </div>
    `;
    reportListEl.querySelector('.load-error-retry').addEventListener('click', loadReports);
    paginationStatusEl.textContent = '';
  }

  // ---------- Period filter ----------
  // Dates are Philippine calendar dates (yyyy-mm-dd), whatever the device's
  // own time zone, to match the office's day.
  const periodSelect = document.getElementById('period-select');
  const periodMonths = document.getElementById('period-months');
  const periodCustom = document.getElementById('period-custom');
  const periodFrom = document.getElementById('period-from');
  const periodTo = document.getElementById('period-to');
  const periodSummary = document.getElementById('period-summary');

  const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July',
    'August', 'September', 'October', 'November', 'December'];
  const MONTHS_LISTED = 24;

  function manilaToday() {
    // en-CA formats as yyyy-mm-dd.
    const s = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila' }).format(new Date());
    const [y, m, d] = s.split('-').map(Number);
    return { y, m, d };
  }
  const pad = n => String(n).padStart(2, '0');
  const iso = (y, m, d) => `${y}-${pad(m)}-${pad(d)}`;
  const lastDay = (y, m) => new Date(Date.UTC(y, m, 0)).getUTCDate(); // m is 1-12
  function shiftMonth(y, m, by) {
    const t = y * 12 + (m - 1) + by;
    return { y: Math.floor(t / 12), m: (t % 12) + 1 };
  }
  function monthRange(y, m) {
    return { from: iso(y, m, 1), to: iso(y, m, lastDay(y, m)), label: `${MONTH_NAMES[m - 1]} ${y}` };
  }
  function prettyDate(value) {
    const [y, m, d] = value.split('-').map(Number);
    return `${MONTH_NAMES[m - 1].slice(0, 3)} ${d}, ${y}`;
  }

  // The last MONTHS_LISTED months, newest first, as "2026-09" options.
  (function fillMonths() {
    const t = manilaToday();
    for (let i = 0; i < MONTHS_LISTED; i++) {
      const { y, m } = shiftMonth(t.y, t.m, -i);
      const opt = document.createElement('option');
      opt.value = `${y}-${pad(m)}`;
      opt.textContent = `${MONTH_NAMES[m - 1]} ${y}`;
      periodMonths.appendChild(opt);
    }
  })();

  // { from, to, label } for the current choice, or null for All time.
  function currentPeriod() {
    const value = periodSelect.value;
    const t = manilaToday();
    if (value === 'this-month') return monthRange(t.y, t.m);
    if (value === 'last-month') { const p = shiftMonth(t.y, t.m, -1); return monthRange(p.y, p.m); }
    if (/^\d{4}-\d{2}$/.test(value)) { const [y, m] = value.split('-').map(Number); return monthRange(y, m); }
    if (value === 'custom' && periodFrom.value && periodTo.value) {
      return { from: periodFrom.value, to: periodTo.value,
        label: `${prettyDate(periodFrom.value)} to ${prettyDate(periodTo.value)}` };
    }
    return null;
  }

  function showPeriodSummary(period) {
    if (!period) { periodSummary.hidden = true; periodSummary.textContent = ''; return; }
    periodSummary.textContent = '';
    periodSummary.append(`Showing activity in ${period.label}. `);
    const clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'period-clear';
    clear.textContent = 'Show all time';
    clear.addEventListener('click', () => { periodSelect.value = 'all'; onPeriodChange(); });
    periodSummary.appendChild(clear);
    periodSummary.hidden = false;
  }

  // Keeps the choice in the address, so a refresh (or a live update) keeps it.
  function rememberPeriod() {
    const url = new URL(window.location.href);
    ['period', 'from', 'to'].forEach(k => url.searchParams.delete(k));
    if (periodSelect.value !== 'all') url.searchParams.set('period', periodSelect.value);
    if (periodSelect.value === 'custom') {
      url.searchParams.set('from', periodFrom.value);
      url.searchParams.set('to', periodTo.value);
    }
    history.replaceState(null, '', url);
  }

  function restorePeriod() {
    const params = new URLSearchParams(window.location.search);
    const value = params.get('period');
    if (!value || ![...periodSelect.options].some(o => o.value === value)) return;
    periodSelect.value = value;
    if (value === 'custom') {
      periodFrom.value = params.get('from') || '';
      periodTo.value = params.get('to') || '';
      periodCustom.hidden = false;
    }
  }

  function onPeriodChange() {
    const custom = periodSelect.value === 'custom';
    periodCustom.hidden = !custom;
    if (custom) {
      if (!periodFrom.value || !periodTo.value) {
        // Start the custom range at this month, ready to adjust.
        const t = manilaToday();
        periodFrom.value = periodFrom.value || iso(t.y, t.m, 1);
        periodTo.value = periodTo.value || iso(t.y, t.m, t.d);
      }
      periodFrom.focus();
      return; // waits for Apply
    }
    rememberPeriod();
    loadReports();
  }

  periodSelect.addEventListener('change', onPeriodChange);
  periodCustom.addEventListener('submit', (e) => {
    e.preventDefault();
    if (periodFrom.value > periodTo.value) {
      periodTo.setCustomValidity('The end date must be on or after the start date.');
      periodTo.reportValidity();
      return;
    }
    periodTo.setCustomValidity('');
    rememberPeriod();
    loadReports();
  });
  periodTo.addEventListener('input', () => periodTo.setCustomValidity(''));

  function loadReports() {
    const period = currentPeriod();
    const url = new URL('/Reports/Data', window.location.origin);
    if (period) {
      url.searchParams.set('from', period.from);
      url.searchParams.set('to', period.to);
    }
    showPeriodSummary(period);

    loaded = false;
    reportListEl.innerHTML = skeletonHtml;
    reportListEl.setAttribute('aria-busy', 'true');
    paginationStatusEl.textContent = 'Loading reports...';
    fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
      .then(res => res.ok ? res.json() : Promise.reject(res.status))
      .then(data => window.ReportsPage.setReports(Array.isArray(data) ? data : []))
      .catch(renderLoadError)
      .finally(() => reportListEl.removeAttribute('aria-busy'));
  }

  restorePeriod();
  loadReports();
})();
