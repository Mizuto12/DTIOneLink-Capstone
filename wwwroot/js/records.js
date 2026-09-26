const PAGE_SIZE = 3; // a new page only appears once the current page holds 3 records

const form = document.getElementById('new-entry-form');
const tableBody = document.getElementById('submissions-body');
const countBadge = document.getElementById('entry-count-badge');
const pageNumbers = document.getElementById('page-numbers');
const paginationStatus = document.getElementById('pagination-status');
const prevBtn = document.getElementById('prev-page-btn');
const nextBtn = document.getElementById('next-page-btn');
const filterForm = document.getElementById('records-filter');

let entries = [];
let currentPage = 1;
let filtersActive = false;

// Basic HTML-escaping so entered text can't break table markup
function escapeHtml(value) {
  const div = document.createElement('div');
  div.textContent = value ?? '';
  return div.innerHTML;
}

// Anti-forgery token rendered inside the entry form; sent as the header
// Program.cs configures (X-CSRF-TOKEN).
function getCsrfToken() {
  const input = form.querySelector('input[name="__RequestVerificationToken"]');
  return input ? input.value : '';
}

// Enable tonal hover effect on a row
function attachHoverEffect(row) {
  // Rows coloured as a retention reminder keep their colour on hover.
  if (row.classList.contains('retention-due-soon') || row.classList.contains('retention-ended')) {
    return;
  }
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
  const legend = document.getElementById('retention-legend');
  if (legend) {
    legend.hidden = true;
  }

  if (entries.length === 0) {
    const emptyRow = document.createElement('tr');
    emptyRow.className = 'empty-row';
    emptyRow.innerHTML = filtersActive
      ? `<td colspan="8" class="empty-state">No records match your search.</td>`
      : `<td colspan="8" class="empty-state">No new records. Fill in the form above to add one.</td>`;
    tableBody.appendChild(emptyRow);
    return;
  }

  const start = (currentPage - 1) * PAGE_SIZE;
  const pageEntries = entries.slice(start, start + PAGE_SIZE);

  pageEntries.forEach(data => {
    const row = document.createElement('tr');
    row.className = 'pill-row';
    row.innerHTML = `
      <td>${escapeHtml(data.code)}</td>
      <td class="title-cell">${escapeHtml(data.title)}</td>
      <td>${escapeHtml(data.medium)}</td>
      <td>${escapeHtml(data.location)}</td>
      <td>${escapeHtml(data.periodCovered)}</td>
      <td>${escapeHtml(data.filingSystem)}</td>
      <td>${escapeHtml(data.accessControl)}</td>
      <td>${escapeHtml(data.retentionPeriod)}</td>
    `;
    // Reminder colour (decided on the server): yellow when the retention
    // period ends within 6 months, red once it has ended. The legend under
    // the table explains the colours; the hover text says the same thing.
    if (data.retentionState === 'due-soon') {
      row.classList.add('retention-due-soon');
      row.title = 'Retention ends within 6 months';
    } else if (data.retentionState === 'ended') {
      row.classList.add('retention-ended');
      row.title = 'Retention period has ended — review for disposal';
    }
    // Records managers also receive the exact date.
    if (data.retentionDueDate) {
      row.title = (row.title ? row.title + ' ' : '') + `(ends ${data.retentionDueDate})`;
    }
    tableBody.appendChild(row);
    attachHoverEffect(row);
  });

  if (legend) {
    legend.hidden = !pageEntries.some(e => e.retentionState === 'due-soon' || e.retentionState === 'ended');
  }
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
  updateSaveMasterlistButton();
}

prevBtn.addEventListener('click', () => goToPage(currentPage - 1));
nextBtn.addEventListener('click', () => goToPage(currentPage + 1));

// Current toolbar values as query-string params (empty ones omitted).
// The server applies these filters and the access rules.
function currentFilterParams() {
  const params = new URLSearchParams();
  if (!filterForm) return params;
  new FormData(filterForm).forEach((value, key) => {
    const trimmed = String(value).trim();
    if (trimmed) params.append(key, trimmed);
  });
  return params;
}

// Load records from the database, applying any toolbar filters
async function loadRecords() {
  try {
    const params = currentFilterParams();
    filtersActive = [...params.keys()].length > 0;
    const query = params.toString();
    const res = await fetch('/Records/GetAll' + (query ? `?${query}` : ''));
    if (!res.ok) throw new Error('Failed to load records');
    entries = await res.json();
    // Start on page 1 so the first-logged records show first.
    currentPage = 1;
    render();
  } catch (err) {
    console.error(err);
  }
}

// Turns a failed response into a message for the user: the server's JSON
// { message } when there is one, otherwise a plain explanation by status
// (e.g. an expired anti-forgery token returns an empty 400).
async function readErrorMessage(res) {
  const body = await res.json().catch(() => null);
  if (body && typeof body.message === 'string' && body.message.trim()) {
    return body.message;
  }

  switch (res.status) {
    case 400: return 'The page is out of date. Please refresh the page and try again.';
    case 401: return 'Your session has expired. Please log in again.';
    case 403: return 'Your account is not allowed to use Records Management.';
    case 409: return 'That Code already exists. Please use a different Code.';
    default:  return 'Unable to save the record. Please contact the administrator.';
  }
}

// ---------- "Other / Specify" choices ----------
// Medium, Filing System and Access Control each have an "Other / Specify"
// option. Choosing it reveals a small text box (and makes it required);
// choosing a standard option hides and clears it again.
const OTHER_VALUE = '__other__';
const otherSelects = [...form.querySelectorAll('select[data-other-input]')];

function otherInputFor(select) {
  return document.getElementById(select.dataset.otherInput);
}

function syncOtherInput(select, focus) {
  const input = otherInputFor(select);
  if (!input) return;
  const isOther = select.value === OTHER_VALUE;
  input.hidden = !isOther;
  input.required = isOther;
  if (!isOther) {
    input.value = '';
  } else if (focus) {
    input.focus();
  }
}

otherSelects.forEach(select => {
  select.addEventListener('change', () => syncOtherInput(select, true));
});

// form.reset() restores the placeholders but not the hidden state, so
// re-sync once the reset has actually happened.
form.addEventListener('reset', () => {
  setTimeout(() => otherSelects.forEach(select => syncOtherInput(select, false)), 0);
});

// The value to save: the chosen option, or the typed text for "Other / Specify".
function chosenValue(select) {
  if (select.value === OTHER_VALUE) {
    return (otherInputFor(select)?.value || '').trim();
  }
  return (select.value || '').trim();
}

form.addEventListener('submit', async (e) => {
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
  const payload = {
    code: formData.get('code').trim(),
    title: formData.get('title').trim(),
    medium: chosenValue(form.elements['medium']),
    location: formData.get('location').trim(),
    periodCovered: formData.get('period').trim(),
    filingSystem: chosenValue(form.elements['filing']),
    accessControl: chosenValue(form.elements['access']),
    retentionPeriod: formData.get('retention').trim()
  };

  try {
    const res = await fetch('/Records/Save', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': getCsrfToken()
      },
      body: JSON.stringify(payload)
    });

    if (!res.ok) {
      throw new Error(await readErrorMessage(res));
    }

    const saved = await res.json();

    btn.innerHTML = `<span class="material-symbols-outlined">check_circle</span> Success!`;

    entries.push(saved);
    // Jump to the page that now contains the new record
    currentPage = totalPages();
    render();

    setTimeout(() => {
      btn.innerHTML = originalContent;
      btn.disabled = false;
      form.reset();
    }, 1200);

  } catch (err) {
    // fetch() itself throws a TypeError when the server can't be reached.
    const message = err instanceof TypeError
      ? 'Unable to reach the server. Please check your connection and try again.'
      : err.message;

    // The button is too small for a full sentence, so it shows a short
    // status and the full reason appears in a dialog that stays until
    // the user closes it.
    btn.innerHTML = `<span class="material-symbols-outlined">error</span> Not saved`;
    alert(message);
    setTimeout(() => {
      btn.innerHTML = originalContent;
      btn.disabled = false;
    }, 1500);
  }
});

if (filterForm) {
  filterForm.addEventListener('submit', (e) => {
    e.preventDefault();
    loadRecords();
  });
  // The reset event fires before the fields are cleared, so reload after.
  filterForm.addEventListener('reset', () => {
    setTimeout(loadRecords, 0);
  });
}

// ---------- Save Masterlist ----------
// Saves every record on the table (all of the user's new records, whatever
// the search shows) into one Excel masterlist, downloads it, and clears the
// table. The records stay in the system; the file is listed under Saved
// Masterlists so it can be downloaded again.
const saveMasterlistBtn = document.getElementById('save-masterlist-btn');
const masterlistDialog = document.getElementById('masterlist-dialog');
const masterlistForm = document.getElementById('masterlist-form');
const masterlistSummary = document.getElementById('masterlist-dialog-summary');
const masterlistConfirmBtn = document.getElementById('masterlist-confirm-btn');
const masterlistList = document.getElementById('masterlist-list');
const masterlistDialogTitle = document.getElementById('masterlist-dialog-title');
const saveMasterlistLabel = document.getElementById('save-masterlist-label');
const openBanner = document.getElementById('open-masterlist-banner');
const openName = document.getElementById('open-masterlist-name');
const stopAddingBtn = document.getElementById('stop-adding-btn');
let masterlistsAvailable = false;
// The saved masterlist reopened with "Add Records" ({ id, fileName }), or null.
let openMasterlist = null;

// Reloads the table (clearing any search) and the saved list.
function refreshAll() {
  if (filterForm) filterForm.reset(); // its reset handler reloads the table
  else loadRecords();
  loadMasterlists();
}

// Banner and button wording for "adding to a saved masterlist" vs a new one.
function applyOpenState() {
  const isOpen = openMasterlist !== null;
  if (openBanner) openBanner.hidden = !isOpen;
  if (openName) openName.textContent = isOpen ? openMasterlist.fileName : '';
  if (saveMasterlistLabel) saveMasterlistLabel.textContent = isOpen ? 'Update Masterlist' : 'Save Masterlist';
}

// POSTs with the anti-forgery token; throws with a readable message on failure.
async function postAction(url) {
  const res = await fetch(url, { method: 'POST', headers: { 'X-CSRF-TOKEN': getCsrfToken() } });
  if (!res.ok) throw new Error(await readErrorMessage(res));
  return res.json();
}

function showActionError(err) {
  alert(err instanceof TypeError
    ? 'Unable to reach the server. Please check your connection and try again.'
    : err.message);
}

// Enabled when there is something to save. While a search is active the
// table may hide some new records, so the button stays usable and the
// server decides.
function updateSaveMasterlistButton() {
  if (!saveMasterlistBtn) return;
  saveMasterlistBtn.disabled = !masterlistsAvailable || (!filtersActive && entries.length === 0);
}

function fillSignatories(values) {
  const v = values || {};
  masterlistForm.elements['preparedByName'].value = v.preparedByName || '';
  masterlistForm.elements['preparedByPosition'].value = v.preparedByPosition || '';
  masterlistForm.elements['reviewedByName'].value = v.reviewedByName || '';
  masterlistForm.elements['reviewedByPosition'].value = v.reviewedByPosition || '';
  masterlistForm.elements['notedByName'].value = v.notedByName || '';
  masterlistForm.elements['notedByPosition'].value = v.notedByPosition || '';
}

function renderMasterlists(items) {
  masterlistList.innerHTML = '';
  if (!items.length) {
    masterlistList.innerHTML = '<li class="masterlist-empty">No saved masterlists yet.</li>';
    return;
  }
  items.forEach(item => {
    const li = document.createElement('li');
    const recordWord = item.recordCount === 1 ? 'record' : 'records';
    const addButton = item.isOpen
      ? ''
      : `<button class="btn btn-outline" type="button" data-add-records="${item.id}">
           <span class="material-symbols-outlined">add</span> Add Records
         </button>`;
    li.innerHTML = `
      <div>
        <div class="masterlist-name">${escapeHtml(item.fileName)}${item.isOpen ? '<span class="masterlist-open-tag">Adding records</span>' : ''}</div>
        <div class="masterlist-meta">Last saved ${escapeHtml(item.savedAt)} · ${item.recordCount} ${recordWord}</div>
      </div>
      <div class="masterlist-actions">
        ${addButton}
        <a class="btn btn-outline" href="/Records/DownloadMasterlist?id=${encodeURIComponent(item.id)}">
          <span class="material-symbols-outlined">download</span> Download
        </a>
      </div>`;
    const add = li.querySelector('[data-add-records]');
    if (add) add.addEventListener('click', () => reopenMasterlist(item));
    masterlistList.appendChild(li);
  });
}

// Loads the saved list and pre-fills the signature names from the last save.
async function loadMasterlists() {
  try {
    const res = await fetch('/Records/Masterlists');
    if (!res.ok) throw new Error('Failed to load saved masterlists');
    const data = await res.json();
    masterlistsAvailable = data.available === true;
    openMasterlist = data.openMasterlist || null;
    applyOpenState();
    renderMasterlists(data.items || []);
    fillSignatories(data.signatories);
    if (saveMasterlistBtn && !masterlistsAvailable) {
      saveMasterlistBtn.title = 'Saving masterlists is not set up yet. Please contact the administrator.';
    }
    updateSaveMasterlistButton();
  } catch (err) {
    console.error(err);
  }
}

// "Add Records": puts a saved masterlist's records back on the table so new
// ones can be added; Update Masterlist then saves one updated file.
async function reopenMasterlist(item) {
  const newCount = openMasterlist ? 0 : entries.length;
  let question = `Add records to "${item.fileName}"?\n\n` +
    'Its saved records will be shown on the table. Add your new records, then click Update Masterlist.';
  if (newCount > 0 || filtersActive) {
    question += '\n\nThe new records already on your table will be added to this masterlist too.';
  }
  if (!confirm(question)) return;
  try {
    await postAction(`/Records/ReopenMasterlist?id=${encodeURIComponent(item.id)}`);
    refreshAll();
  } catch (err) {
    showActionError(err);
  }
}

if (stopAddingBtn) {
  stopAddingBtn.addEventListener('click', async () => {
    try {
      await postAction('/Records/CloseMasterlist');
      refreshAll();
    } catch (err) {
      showActionError(err);
    }
  });
}

if (saveMasterlistBtn && masterlistDialog) {
  saveMasterlistBtn.addEventListener('click', () => {
    const n = entries.length;
    if (openMasterlist) {
      masterlistDialogTitle.textContent = 'Update Masterlist';
      masterlistSummary.textContent = `This saves "${openMasterlist.fileName}" again with your new records added, and clears the table. The old file is replaced by the updated one.`;
      masterlistConfirmBtn.lastChild.textContent = ' Update and Download';
    } else {
      masterlistDialogTitle.textContent = 'Save Masterlist';
      masterlistSummary.textContent = filtersActive
        ? 'This saves ALL your new records (not only the ones your search shows) into an Excel file and clears the table. The records stay saved in the system.'
        : `This saves the ${n} ${n === 1 ? 'record' : 'records'} on the table into an Excel file and clears the table. The records stay saved in the system.`;
      masterlistConfirmBtn.lastChild.textContent = ' Save and Download';
    }
    masterlistDialog.showModal();
  });

  document.getElementById('masterlist-cancel-btn').addEventListener('click', () => masterlistDialog.close());

  masterlistForm.addEventListener('submit', async (e) => {
    e.preventDefault();

    const original = masterlistConfirmBtn.innerHTML;
    masterlistConfirmBtn.innerHTML = `<span class="material-symbols-outlined animate-spin">sync</span> Saving...`;
    masterlistConfirmBtn.disabled = true;

    const payload = {};
    new FormData(masterlistForm).forEach((value, key) => { payload[key] = String(value).trim(); });

    try {
      const res = await fetch('/Records/SaveMasterlist', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': getCsrfToken()
        },
        body: JSON.stringify(payload)
      });
      if (!res.ok) {
        throw new Error(await readErrorMessage(res));
      }
      const saved = await res.json();

      masterlistDialog.close();
      // Start the download, then show the cleared table and the new file.
      window.location.href = `/Records/DownloadMasterlist?id=${encodeURIComponent(saved.masterlistId)}`;
      refreshAll();
    } catch (err) {
      const message = err instanceof TypeError
        ? 'Unable to reach the server. Please check your connection and try again.'
        : err.message;
      alert(message);
    } finally {
      masterlistConfirmBtn.innerHTML = original;
      masterlistConfirmBtn.disabled = false;
    }
  });
}

loadMasterlists();
loadRecords();