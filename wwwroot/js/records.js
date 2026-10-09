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

// The table starts with placeholder rows (from the page); they show again
// on every reload, so slow wifi never shows "No new records" by mistake.
const tableSkeletonHtml = tableBody.innerHTML;
let recordsLoading = false;

// "Couldn't load" message with a Try again button (styles in adminlayout.css).
function loadErrorHtml(what) {
  return `
    <div class="load-error" role="alert">
      <span class="material-symbols-outlined">error</span>
      <p>Couldn't load ${what}. Check your connection.</p>
      <button type="button" class="load-error-retry">Try again</button>
    </div>`;
}

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
    // data-label: the column name, shown on phones where rows become cards.
    row.innerHTML = `
      <td data-label="Code">${escapeHtml(data.code)}</td>
      <td class="title-cell" data-label="Title of Record">${escapeHtml(data.title)}</td>
      <td data-label="Medium">${escapeHtml(data.medium)}</td>
      <td data-label="Location">${escapeHtml(data.location)}</td>
      <td data-label="Period Covered">${escapeHtml(data.periodCovered)}</td>
      <td data-label="Filing System">${escapeHtml(data.filingSystem)}</td>
      <td data-label="Access Control">${escapeHtml(data.accessControl)}</td>
      <td data-label="Retention Period">${escapeHtml(data.retentionPeriod)}</td>
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
  if (recordsLoading) return;
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
  recordsLoading = true;
  tableBody.innerHTML = tableSkeletonHtml;
  tableBody.setAttribute('aria-busy', 'true');
  const legend = document.getElementById('retention-legend');
  if (legend) legend.hidden = true;
  try {
    const params = currentFilterParams();
    filtersActive = [...params.keys()].length > 0;
    const query = params.toString();
    const res = await fetch('/Records/GetAll' + (query ? `?${query}` : ''));
    if (!res.ok) throw new Error('Failed to load records');
    entries = await res.json();
    // Start on page 1 so the first-logged records show first.
    currentPage = 1;
    recordsLoading = false;
    render();
  } catch (err) {
    console.error(err);
    recordsLoading = false;
    tableBody.innerHTML = `<tr class="empty-row"><td colspan="8" class="empty-state">${loadErrorHtml('records')}</td></tr>`;
    tableBody.querySelector('.load-error-retry').addEventListener('click', loadRecords);
  } finally {
    tableBody.removeAttribute('aria-busy');
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
    // The button is too small for a full sentence, so it shows a short
    // status and the full reason appears in a dialog that stays until
    // the user closes it. (fetch() throws a TypeError when offline.)
    btn.innerHTML = `<span class="material-symbols-outlined">error</span> Not saved`;
    showError('Record not saved', errorMessage(err));
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
const masterlistSkeletonHtml = masterlistList ? masterlistList.innerHTML : '';
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

// ---------- In-page notice dialog (replaces the browser's alert/confirm) ----------
// showNotice({ tone, icon, title, file, message, steps, note, okText, cancelText })
// resolves true when OK is pressed, false when cancelled or closed. Leave
// cancelText out for a message with a single OK button.
const noticeDialog = document.getElementById('notice-dialog');

function showNotice(opts) {
  if (!noticeDialog) {
    // Fallback if the dialog markup is missing.
    const text = [opts.title, opts.message].filter(Boolean).join('\n\n');
    return Promise.resolve(opts.cancelText ? confirm(text) : (alert(text), true));
  }

  const el = id => document.getElementById(id);
  const setText = (id, value) => {
    const node = el(id);
    node.textContent = value || '';
    node.hidden = !value;
  };

  noticeDialog.dataset.tone = opts.tone || 'info';
  el('notice-icon').textContent = opts.icon || 'info';
  el('notice-title').textContent = opts.title || '';
  setText('notice-file', opts.file);
  setText('notice-message', opts.message);
  setText('notice-note', opts.note);

  const steps = el('notice-steps');
  steps.innerHTML = '';
  (opts.steps || []).forEach((step, i) => {
    const li = document.createElement('li');
    const num = document.createElement('span');
    num.className = 'notice-step-num';
    num.textContent = String(i + 1);
    li.appendChild(num);
    li.appendChild(document.createTextNode(step));
    steps.appendChild(li);
  });
  steps.hidden = !(opts.steps && opts.steps.length);

  const okBtn = el('notice-ok');
  const cancelBtn = el('notice-cancel');
  okBtn.textContent = opts.okText || 'OK';
  cancelBtn.textContent = opts.cancelText || 'Cancel';
  cancelBtn.hidden = !opts.cancelText;

  return new Promise(resolve => {
    let result = false;
    const finish = value => { result = value; noticeDialog.close(); };
    const onOk = () => finish(true);
    const onCancel = () => finish(false);
    const onBackdrop = e => { if (e.target === noticeDialog) finish(false); };
    const onClose = () => {
      okBtn.removeEventListener('click', onOk);
      cancelBtn.removeEventListener('click', onCancel);
      noticeDialog.removeEventListener('click', onBackdrop);
      resolve(result);
    };
    okBtn.addEventListener('click', onOk);
    cancelBtn.addEventListener('click', onCancel);
    noticeDialog.addEventListener('click', onBackdrop);
    noticeDialog.addEventListener('close', onClose, { once: true });
    noticeDialog.showModal();
    okBtn.focus();
  });
}

function errorMessage(err) {
  return err instanceof TypeError
    ? 'Unable to reach the server. Please check your connection and try again.'
    : err.message;
}

function showError(title, message) {
  return showNotice({ tone: 'danger', icon: 'error', title, message, okText: 'OK' });
}

function showActionError(err) {
  showError('Something went wrong', errorMessage(err));
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
      <div class="masterlist-info">
        <div class="masterlist-name" data-name-text>${escapeHtml(item.fileName)}${item.isOpen ? '<span class="masterlist-open-tag">Adding records</span>' : ''}</div>
        <div class="masterlist-meta">Last saved ${escapeHtml(item.savedAt)} · ${item.recordCount} ${recordWord}</div>
      </div>
      <div class="masterlist-actions">
        <button class="btn btn-icon" type="button" data-rename="${item.id}" aria-label="Rename ${escapeHtml(item.fileName)}" title="Rename">
          <span class="material-symbols-outlined">edit</span>
        </button>
        <button class="btn btn-icon" type="button" data-print="${item.id}" aria-label="Print ${escapeHtml(item.fileName)}" title="Print">
          <span class="material-symbols-outlined">print</span>
        </button>
        ${addButton}
        <a class="btn btn-outline" href="/Records/DownloadMasterlist?id=${encodeURIComponent(item.id)}" data-download="${item.id}">
          <span class="material-symbols-outlined">download</span> Download
        </a>
      </div>`;
    const add = li.querySelector('[data-add-records]');
    if (add) add.addEventListener('click', () => reopenMasterlist(item));
    const rename = li.querySelector('[data-rename]');
    if (rename) rename.addEventListener('click', () => openRenameMasterlist(item));
    const print = li.querySelector('[data-print]');
    if (print) print.addEventListener('click', () => printSavedMasterlist(item.id));
    const download = li.querySelector('[data-download]');
    // Chrome/Edge: let the user pick where to save instead of it silently
    // landing in Downloads. The href stays as a plain-download fallback for
    // browsers without the File System Access API (Firefox, Safari).
    if (download && supportsSaveFilePicker) {
      download.addEventListener('click', e => {
        e.preventDefault();
        downloadMasterlistWithPicker(item.id, item.fileName);
      });
    }
    masterlistList.appendChild(li);
  });
}

// ---------- Download: let the user choose where to save (Chrome/Edge) ----------
// The File System Access API cannot reveal or open a file's location in
// Explorer (no browser exposes real filesystem paths to a web page) — it can
// only let the user pick a save destination up front, which is what this does.
const supportsSaveFilePicker = typeof window.showSaveFilePicker === 'function';

async function downloadMasterlistWithPicker(id, fileName) {
  const url = `/Records/DownloadMasterlist?id=${encodeURIComponent(id)}`;
  try {
    const res = await fetch(url);
    if (!res.ok) throw new Error(await readErrorMessage(res));
    const blob = await res.blob();
    const handle = await window.showSaveFilePicker({
      suggestedName: fileName,
      types: [{
        description: 'Excel Workbook',
        accept: { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] }
      }]
    });
    const writable = await handle.createWritable();
    await writable.write(blob);
    await writable.close();
  } catch (err) {
    if (err && err.name === 'AbortError') return; // user closed the picker
    window.location.href = url; // picker failed for some other reason — fall back
  }
}

// ---------- Rename Masterlist ----------
// A small dialog (same pattern as Save Masterlist) so renaming never has to
// fight for space inside the narrow Saved Masterlists row.
const renameDialog = document.getElementById('rename-masterlist-dialog');
const renameForm = document.getElementById('rename-masterlist-form');
const renameInput = document.getElementById('rename-masterlist-input');
let renamingItem = null;

function openRenameMasterlist(item) {
  if (!renameDialog) return;
  renamingItem = item;
  const baseName = item.fileName.replace(/\.xlsx$/i, '');
  renameInput.value = baseName;
  renameDialog.showModal();
  renameInput.focus();
  renameInput.select();
}

if (renameDialog && renameForm) {
  document.getElementById('rename-cancel-btn').addEventListener('click', () => renameDialog.close());

  renameForm.addEventListener('submit', async e => {
    e.preventDefault();
    if (!renamingItem) return;
    const newBaseName = renameInput.value.trim();
    if (!newBaseName) { renameInput.focus(); return; }
    const newFileName = newBaseName.toLowerCase().endsWith('.xlsx') ? newBaseName : `${newBaseName}.xlsx`;

    const confirmBtn = document.getElementById('rename-confirm-btn');
    const original = confirmBtn.innerHTML;
    confirmBtn.innerHTML = `<span class="material-symbols-outlined animate-spin">sync</span> Saving...`;
    confirmBtn.disabled = true;
    try {
      const res = await fetch(`/Records/RenameMasterlist?id=${encodeURIComponent(renamingItem.id)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getCsrfToken() },
        body: JSON.stringify({ fileName: newFileName })
      });
      if (!res.ok) throw new Error(await readErrorMessage(res));
      renameDialog.close();
      loadMasterlists();
    } catch (err) {
      showError('Could not rename file', errorMessage(err));
    } finally {
      confirmBtn.innerHTML = original;
      confirmBtn.disabled = false;
    }
  });
}

// Loads the saved list and pre-fills the signature names from the last save.
async function loadMasterlists() {
  masterlistList.innerHTML = masterlistSkeletonHtml;
  masterlistList.setAttribute('aria-busy', 'true');
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
    masterlistList.innerHTML = `<li class="masterlist-empty">${loadErrorHtml('saved masterlists')}</li>`;
    masterlistList.querySelector('.load-error-retry').addEventListener('click', loadMasterlists);
  } finally {
    masterlistList.removeAttribute('aria-busy');
  }
}

// "Add Records": puts a saved masterlist's records back on the table so new
// ones can be added; Update Masterlist then saves one updated file.
async function reopenMasterlist(item) {
  const newCount = openMasterlist ? 0 : entries.length;
  const ok = await showNotice({
    tone: 'info',
    icon: 'folder_open',
    title: 'Add records to this masterlist?',
    file: item.fileName,
    steps: [
      'Its saved records will appear on the table.',
      'Add your new records using the form.',
      'Click Update Masterlist to save the updated file.'
    ],
    note: (newCount > 0 || filtersActive)
      ? 'The new records already on your table will be added to this masterlist too.'
      : '',
    okText: 'Add Records',
    cancelText: 'Cancel'
  });
  if (!ok) return;
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
      showError('Masterlist not saved', errorMessage(err));
    } finally {
      masterlistConfirmBtn.innerHTML = original;
      masterlistConfirmBtn.disabled = false;
    }
  });
}

// A Saved Masterlists row's print icon: loads a server-built PDF of that
// file (Services/RecordMasterlistPdf.cs mirrors the Excel layout exactly)
// into a hidden iframe and triggers the browser's print dialog directly,
// instead of opening a new tab the user has to print from themselves.
function printSavedMasterlist(id) {
  const url = `/Records/PrintMasterlist?id=${encodeURIComponent(id)}`;
  const iframe = document.createElement('iframe');
  iframe.style.position = 'fixed';
  iframe.style.right = '0';
  iframe.style.bottom = '0';
  iframe.style.width = '0';
  iframe.style.height = '0';
  iframe.style.border = '0';
  iframe.src = url;
  iframe.onload = () => {
    try {
      iframe.contentWindow.focus();
      iframe.contentWindow.print();
    } catch {
      window.open(url, '_blank'); // fallback if the browser blocks printing from the iframe
    }
  };
  document.body.appendChild(iframe);
  // No reliable cross-browser "print dialog closed" event for a PDF inside
  // an iframe, so just clean it up well after the dialog would be done.
  window.setTimeout(() => iframe.remove(), 60000);
}

loadMasterlists();
loadRecords();