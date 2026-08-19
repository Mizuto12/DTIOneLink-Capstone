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

  // ---------- Filtering ----------
  function getFilteredReports() {
    return reports.filter(r => {
      const matchesCategory = currentFilter === 'All Categories' || r.category === currentFilter;
      const matchesSearch = searchTerm === '' || r.title.toLowerCase().includes(searchTerm.toLowerCase());
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
    const filtered = getFilteredReports();
    const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
    if (currentPage > totalPages) currentPage = totalPages;

    reportListEl.innerHTML = '';

    if (filtered.length === 0) {
      const message = reports.length === 0
        ? 'No reports yet.'
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
  //   icon: 'schedule', tone: 'primary', time: '2 hours ago', category: 'Audit Logs' });
  window.ReportsPage = {
    addReport(report) {
      reports.push(report);
      render();
    },
    setReports(newReports) {
      reports = newReports;
      currentPage = 1;
      render();
    },
    getReports() {
      return reports.slice();
    }
  };

  render();
})();
