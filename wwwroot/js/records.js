  const PAGE_SIZE = 3; // a new page only appears once the current page holds 3 records

  const form = document.getElementById('new-entry-form');
  const tableBody = document.getElementById('submissions-body');
  const countBadge = document.getElementById('entry-count-badge');
  const pageNumbers = document.getElementById('page-numbers');
  const paginationStatus = document.getElementById('pagination-status');
  const prevBtn = document.getElementById('prev-page-btn');
  const nextBtn = document.getElementById('next-page-btn');

  let entries = [];
  let currentPage = 1;

  // Basic HTML-escaping so entered text can't break table markup
  function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value;
    return div.innerHTML;
  }

  // Enable tonal hover effect on a row
  function attachHoverEffect(row) {
    row.addEventListener('mouseenter', () => {
      row.querySelectorAll('td').forEach(td => { td.style.background = '#ffffff'; });
    });
    row.addEventListener('mouseleave', () => {
      row.querySelectorAll('td').forEach(td => { td.style.background = ''; });
    });
  }

  function totalPages() {
    return Math.max(1, Math.ceil(entries.length / PAGE_SIZE));
  }

  // Build the visible row(s) for the current page
  function renderRows() {
    tableBody.innerHTML = '';

    if (entries.length === 0) {
      const emptyRow = document.createElement('tr');
      emptyRow.className = 'empty-row';
      emptyRow.innerHTML = `<td colspan="8" class="empty-state">No records yet. Fill in the form above and commit to add one.</td>`;
      tableBody.appendChild(emptyRow);
      return;
    }

    const start = (currentPage - 1) * PAGE_SIZE;
    const pageEntries = entries.slice(start, start + PAGE_SIZE);

    pageEntries.forEach(data => {
      const row = document.createElement('tr');
      row.className = 'pill-row';
      row.innerHTML = `
        <td>${data.code}</td>
        <td class="title-cell">${data.title}</td>
        <td>${data.medium}</td>
        <td>${data.location}</td>
        <td>${data.period}</td>
        <td>${data.filing}</td>
        <td>${data.access}</td>
        <td>${data.retention}</td>
      `;
      tableBody.appendChild(row);
      attachHoverEffect(row);
    });
  }

  // Build the numbered page buttons, truncating with "..." for long ranges
  function renderPageNumbers() {
    pageNumbers.innerHTML = '';
    const total = totalPages();

    function makeBtn(num) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'page-btn' + (num === currentPage ? ' active' : '');
      btn.textContent = num;
      btn.addEventListener('click', () => goToPage(num));
      pageNumbers.appendChild(btn);
    }

    function makeEllipsis() {
      const span = document.createElement('button');
      span.type = 'button';
      span.className = 'page-btn ellipsis';
      span.textContent = '...';
      span.disabled = true;
      pageNumbers.appendChild(span);
    }

    if (total <= 5) {
      for (let i = 1; i <= total; i++) makeBtn(i);
      return;
    }

    makeBtn(1);
    if (currentPage > 3) makeEllipsis();

    const rangeStart = Math.max(2, currentPage - 1);
    const rangeEnd = Math.min(total - 1, currentPage + 1);
    for (let i = rangeStart; i <= rangeEnd; i++) makeBtn(i);

    if (currentPage < total - 2) makeEllipsis();
    makeBtn(total);
  }

  function goToPage(num) {
    const total = totalPages();
    currentPage = Math.min(Math.max(1, num), total);
    render();
  }

  function render() {
    const total = totalPages();
    if (currentPage > total) currentPage = total;

    renderRows();
    renderPageNumbers();

    paginationStatus.textContent = `Page ${currentPage} of ${total}`;
    prevBtn.disabled = currentPage === 1;
    nextBtn.disabled = currentPage === total;

    const start = entries.length === 0 ? 0 : (currentPage - 1) * PAGE_SIZE + 1;
    const end = Math.min(currentPage * PAGE_SIZE, entries.length);
    const shownOnPage = entries.length === 0 ? 0 : end - start + 1;
    countBadge.textContent = `Showing ${shownOnPage} of ${entries.length} entries`;
  }

  prevBtn.addEventListener('click', () => goToPage(currentPage - 1));
  nextBtn.addEventListener('click', () => goToPage(currentPage + 1));

  form.addEventListener('submit', (e) => {
    e.preventDefault();

    // Require every field to be filled before allowing a commit
    if (!form.checkValidity()) {
      form.reportValidity();
      return;
    }

    const btn = form.querySelector('button[type="submit"]');
    const originalContent = btn.innerHTML;
    btn.innerHTML = `<span class="material-symbols-outlined animate-spin">sync</span> Recording...`;
    btn.disabled = true;

    const formData = new FormData(form);
    const entry = {
      code: escapeHtml(formData.get('code').trim()),
      title: escapeHtml(formData.get('title').trim()),
      medium: escapeHtml(formData.get('medium')),
      location: escapeHtml(formData.get('location').trim()),
      period: escapeHtml(formData.get('period').trim()),
      filing: escapeHtml(formData.get('filing')),
      access: escapeHtml(formData.get('access')),
      retention: escapeHtml(formData.get('retention').trim())
    };

    setTimeout(() => {
      btn.innerHTML = `<span class="material-symbols-outlined">check_circle</span> Success!`;

      entries.push(entry);
      // Jump to the page that now contains the new record
      currentPage = totalPages();
      render();

      setTimeout(() => {
        btn.innerHTML = originalContent;
        btn.disabled = false;
        form.reset();
      }, 2000);
    }, 1500);
  });

  render();
