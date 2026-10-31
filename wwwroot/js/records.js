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
let editingRecordId = null; // non-null while the Add New Entry form is editing an existing record

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

// Enable tonal hover effect on a row. The Retention cell carries its own
// conditional-formatting fill (see retentionCellHtml) and keeps it on hover.
function attachHoverEffect(row) {
  row.addEventListener('mouseenter', () => {
    row.querySelectorAll('td:not(.retention-td)').forEach(td => { td.style.background = '#ffffff'; });
  });
  row.addEventListener('mouseleave', () => {
    row.querySelectorAll('td:not(.retention-td)').forEach(td => { td.style.background = ''; });
  });
}

function totalPages() {
  return Math.max(1, Math.ceil(entries.length / PAGE_SIZE));
}

// Status column: always shown — disposal is the record owner's call (see
// RecordsController.SetDisposalStatus), and every user only sees records
// they logged. Approve stays PD-only (ApproveRecords); the disposal buttons
// are gated per-row by data.canDispose, which the server computed as
// "this record's own creator."
const canApproveRecords = tableBody.dataset.canApprove === 'true';
const showStatusColumn = true;

function statusCellHtml(data) {
  const parts = [];
  // Slugified (spaces -> hyphens): the new multi-word statuses ("Disposal
  // Requested", "Awaiting NAP Authority", "Authorized for Disposal") would
  // otherwise split a raw-text class attribute into multiple classes.
  const statusSlug = (data.recordStatus || 'Retention').toLowerCase().trim().replace(/\s+/g, '-');
  parts.push(`<span class="status-pill status-${escapeHtml(statusSlug)}">${escapeHtml(data.recordStatus || 'Retention')}</span>`);

  if (canApproveRecords) {
    parts.push(data.approvedByName
      ? `<span class="approved-note">Approved by ${escapeHtml(data.approvedByName)}${data.approvedAt ? ' on ' + escapeHtml(data.approvedAt) : ''}</span>`
      : `<button type="button" class="btn btn-outline btn-small approve-btn" data-id="${data.recordId}">Approve</button>`);
  } else if (data.approvedByName) {
    parts.push(`<span class="approved-note">Approved by ${escapeHtml(data.approvedByName)}</span>`);
  }

  if (data.canDispose) {
    if (data.recordStatus === 'Retention') {
      // Disposal and Archive are the two real outcomes once a record's
      // retention period is due — temporary records eventually get
      // disposed of, permanent ones get archived instead (RA 9470/NAP).
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkForDisposal">Mark for Disposal</button>`);
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkArchived">Mark as Archived</button>`);
    } else if (data.recordStatus === 'Disposal') {
      // RA 9470/NAP pipeline: a request for disposal authority must be
      // submitted and approved before actual disposal — Mark Disposed is
      // no longer reachable directly from here (see RecordDisposalWorkflow).
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkDisposalRequested">Request Disposal</button>`);
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkArchived">Mark as Archived</button>`);
    } else if (data.recordStatus === 'Disposal Requested') {
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkAwaitingNapAuthority">Send to NAP</button>`);
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkArchived">Mark as Archived</button>`);
    } else if (data.recordStatus === 'Awaiting NAP Authority') {
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn authorize-disposal-btn" data-id="${data.recordId}">Authorized for Disposal</button>`);
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkArchived">Mark as Archived</button>`);
    } else if (data.recordStatus === 'Authorized for Disposal') {
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkDisposed">Mark Disposed</button>`);
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="MarkArchived">Mark as Archived</button>`);
    } else if (data.recordStatus === 'Archived') {
      // The one disposal-status transition with a way back — Disposed
      // stays final (see RestoreToRetention in RecordsController).
      parts.push(`<button type="button" class="btn btn-outline btn-small disposal-btn" data-id="${data.recordId}" data-action="RestoreToRetention">Restore to Retention</button>`);
    }
  }

  parts.push(`<a class="btn btn-outline btn-small" href="/Records/Timeline/${data.recordId}" target="_blank" rel="noopener">History</a>`);

  return parts.join(' ');
}

function mediumBadgeHtml(medium) {
  const esc = escapeHtml(medium);
  // Same truncate-with-hover pattern as Access Control's badge — "Hard
  // Copy/Electronic" and a typed "Other" value both run longer than
  // medium-col's actual width.
  return `<span class="badge badge-medium" title="${esc}">${esc}</span>`;
}

// Access Control is usually one of the four standard choices, but "Other /
// Specify" lets staff type anything, so unrecognized values fall back to a
// neutral badge rather than guessing a color for text we don't control.
function accessToneClass(access) {
  switch ((access || '').trim().toLowerCase()) {
    case 'confidential': return 'access-confidential';
    case 'public record': return 'access-public';
    case 'exclusive': return 'access-exclusive';
    case 'dti only': return 'access-dti';
    default: return 'access-other';
  }
}

function accessBadgeHtml(access) {
  const esc = escapeHtml(access);
  // A typed "Other / Specify" value (e.g. "DTI use only; Public upon
  // request") can run much longer than the four standard choices — title
  // carries the full text on hover, same pattern as the truncated columns.
  return `<span class="badge ${accessToneClass(access)}" title="${esc}">${esc}</span>`;
}

// Retention's tone (ok/due-soon/ended) drives both the icon and the cell's
// conditional-formatting-style fill (applied to the <td> itself — see
// retentionCellHtml and renderRows).
function retentionTone(data) {
  const state = data.retentionState;
  return state === 'ended' ? 'retention-over' : state === 'due-soon' ? 'retention-soon' : 'retention-ok';
}

// Records logged before the date picker was added can still hold a long
// free-text duration ("5 years after contract of winner had been
// terminated/settled, others are disposed after 1 year") — truncate with
// an ellipsis and keep the full text on hover (the same pattern truncHtml
// already uses for Title/Location) instead of letting it blow out the row
// height. New records just show their due date, which is short enough to
// never truncate in practice.
function retentionCellHtml(data) {
  const tone = retentionTone(data);
  const icon = tone === 'retention-over' ? 'warning' : tone === 'retention-soon' ? 'schedule' : 'check_circle';
  const esc = escapeHtml(data.retentionPeriod);
  return `<span class="retention-cell ${tone}"><span class="material-symbols-outlined" aria-hidden="true">${icon}</span><span class="trunc trunc-retention" title="${esc}">${esc}</span></span>`;
}

// Title and Location can run long; truncate with an ellipsis and keep the
// full text on hover instead of forcing the column wider or the row taller.
function truncHtml(value, variant) {
  const esc = escapeHtml(value);
  return `<span class="trunc${variant ? ` trunc-${variant}` : ''}" title="${esc}">${esc}</span>`;
}

function actionsCellHtml(data) {
  if (!data.canEdit) {
    return '<span class="actions-none" aria-hidden="true">—</span>';
  }
  return `
    <div class="row-actions">
      <button type="button" class="row-action-btn edit-record-btn" data-id="${data.recordId}"
              title="Edit" aria-label="Edit ${escapeHtml(data.code)}">
        <span class="material-symbols-outlined">edit</span>
      </button>
      <button type="button" class="row-action-btn attach-file-btn" data-id="${data.recordId}"
              title="Attach a file" aria-label="Attach a file to ${escapeHtml(data.code)}">
        <!-- "attach_file" isn't in the trimmed icon-font subset (see
             wwwroot/css/fonts.css's icon_names list) and falls back to
             literal text; "upload" is already in the subset and used for
             the same "attach a file" concept elsewhere in the app. -->
        <span class="material-symbols-outlined">upload</span>
      </button>
      <input type="file" class="attach-file-input" data-id="${data.recordId}" hidden
             accept=".pdf,.docx,.xlsx,.jpg,.jpeg,.png" />
      <button type="button" class="row-action-btn row-action-danger delete-record-btn" data-id="${data.recordId}" data-code="${escapeHtml(data.code)}"
              title="Delete" aria-label="Delete ${escapeHtml(data.code)}">
        <span class="material-symbols-outlined">delete</span>
      </button>
    </div>
  `;
}

// Build the visible row(s) for the current page
function renderRows() {
  tableBody.innerHTML = '';
  const legend = document.getElementById('retention-legend');
  if (legend) {
    legend.hidden = true;
  }
  const colCount = (showStatusColumn ? 10 : 9) + 1; // +1 for the row-number column

  if (entries.length === 0) {
    const emptyRow = document.createElement('tr');
    emptyRow.className = 'empty-row';
    emptyRow.innerHTML = filtersActive
      ? `<td colspan="${colCount}" class="empty-state">No records match your search.</td>`
      : `<td colspan="${colCount}" class="empty-state">No new records. Fill in the form above to add one.</td>`;
    tableBody.appendChild(emptyRow);
    return;
  }

  const start = (currentPage - 1) * PAGE_SIZE;
  const pageEntries = entries.slice(start, start + PAGE_SIZE);

  pageEntries.forEach((data, idx) => {
    const row = document.createElement('tr');
    row.className = 'pill-row';
    const rowNumber = start + idx + 1;
    const retentionTd = retentionTone(data);
    // data-label: the column name, shown on phones where rows become cards.
    row.innerHTML = `
      <td class="rownum-cell">${rowNumber}</td>
      <td class="code-cell" data-label="Code">${escapeHtml(data.code)}</td>
      <td class="title-cell" data-label="Title of Record">${truncHtml(data.title, 'wide')}</td>
      <td data-label="Medium">${mediumBadgeHtml(data.medium)}</td>
      <td data-label="Location">${truncHtml(data.location)}</td>
      <td data-label="Period Covered">${escapeHtml(data.periodCovered)}</td>
      <td data-label="Filing System">${truncHtml(data.filingSystem, 'filing')}</td>
      <td data-label="Access Control">${accessBadgeHtml(data.accessControl)}</td>
      <td class="retention-td ${retentionTd}" data-label="Retention Period">${retentionCellHtml(data)}</td>
      ${showStatusColumn ? `<td data-label="Status">${statusCellHtml(data)}</td>` : ''}
      <td data-label="Actions">${actionsCellHtml(data)}</td>
    `;
    // Reminder (decided on the server): the Retention cell's own fill above
    // already shows yellow when the period ends within 6 months and red
    // once it has ended; the row keeps the same wording as a hover tooltip.
    if (data.retentionState === 'due-soon') {
      row.title = 'Retention ends within 6 months';
    } else if (data.retentionState === 'ended') {
      row.title = 'Retention period has ended — review for disposal';
    }
    // Records managers also receive the exact date.
    if (data.retentionDueDate) {
      row.title = (row.title ? row.title + ' ' : '') + `(ends ${data.retentionDueDate})`;
    }
    tableBody.appendChild(row);
    attachHoverEffect(row);
  });

  if (showStatusColumn) {
    tableBody.querySelectorAll('.approve-btn').forEach(btn => {
      btn.addEventListener('click', () => handleStatusAction(btn, `/Records/Approve?id=${encodeURIComponent(btn.dataset.id)}`));
    });
    // .authorize-disposal-btn has no data-action (it needs to collect the
    // authority reference first) — wired separately below instead.
    tableBody.querySelectorAll('.disposal-btn:not(.authorize-disposal-btn)').forEach(btn => {
      btn.addEventListener('click', () => handleStatusAction(btn, `/Records/${btn.dataset.action}?id=${encodeURIComponent(btn.dataset.id)}`));
    });
    tableBody.querySelectorAll('.authorize-disposal-btn').forEach(btn => {
      btn.addEventListener('click', () => handleAuthorizeForDisposal(btn));
    });
  }
  tableBody.querySelectorAll('.edit-record-btn').forEach(btn => {
    btn.addEventListener('click', () => beginEdit(btn.dataset.id));
  });
  tableBody.querySelectorAll('.delete-record-btn').forEach(btn => {
    btn.addEventListener('click', () => handleDelete(btn));
  });
  tableBody.querySelectorAll('.attach-file-btn').forEach(btn => {
    btn.addEventListener('click', () => btn.parentElement.querySelector('.attach-file-input').click());
  });
  tableBody.querySelectorAll('.attach-file-input').forEach(input => {
    input.addEventListener('change', () => handleAttachFile(input));
  });

  if (legend) {
    legend.hidden = !pageEntries.some(e => e.retentionState === 'due-soon' || e.retentionState === 'ended');
  }
}

// Shared handler for the Approve/Mark for Disposal/Mark Disposed buttons.
async function handleStatusAction(btn, url) {
  btn.disabled = true;
  try {
    await postAction(url);
    await loadRecords();
  } catch (err) {
    btn.disabled = false;
    await showNotice({
      tone: 'error',
      icon: 'error',
      title: 'Couldn’t update the record',
      message: err.message || 'Please try again.',
      okText: 'OK'
    });
  }
}

// "Authorized for Disposal" is the one transition that records a free-text
// paper trail (decision 4: honest record only, never validated against a
// real NAP reference format) — collected with two plain prompts rather
// than a whole new dialog, matching this app's existing native-dialog-first,
// minimal-dependency approach.
async function handleAuthorizeForDisposal(btn) {
  const authorityReference = window.prompt('NAP disposal authority reference (required):');
  if (authorityReference === null) {
    return; // cancelled — no transition attempted
  }
  if (!authorityReference.trim()) {
    await showNotice({
      tone: 'error', icon: 'error', title: 'Authority reference needed',
      message: 'Enter the NAP disposal authority reference to continue, or cancel.', okText: 'OK'
    });
    return;
  }
  const notes = window.prompt('Notes (optional):') || '';

  const params = new URLSearchParams({ id: btn.dataset.id, authorityReference, notes });
  await handleStatusAction(btn, `/Records/MarkAuthorizedForDisposal?${params.toString()}`);
}

// Decision 7: the attachment belongs to the record itself, so it's
// reusable everywhere that record shows up — not tied to any one
// masterlist save.
async function handleAttachFile(input) {
  const file = input.files && input.files[0];
  if (!file) {
    return;
  }

  const body = new FormData();
  body.append('recordId', input.dataset.id);
  body.append('file', file);
  body.append('__RequestVerificationToken', getCsrfToken());

  try {
    const res = await fetch('/Records/UploadAttachment', { method: 'POST', body });
    if (!res.ok) throw new Error(await readErrorMessage(res));
    await showNotice({ tone: 'success', icon: 'check_circle', title: 'File attached', message: `${file.name} was attached to this record.`, okText: 'OK' });
  } catch (err) {
    await showNotice({ tone: 'error', icon: 'error', title: 'Couldn’t attach the file', message: err.message || 'Please try again.', okText: 'OK' });
  } finally {
    input.value = '';
  }
}

// Sets a dropdown (+ its "Other / Specify" text box) to a stored value that
// may or may not match one of the standard options — the same ambiguity the
// stored data itself can have, since typed "Other" values are kept verbatim.
function setChoiceField(select, value) {
  const isStandard = [...select.options].some(o => o.value === value);
  select.value = isStandard ? value : OTHER_VALUE;
  syncOtherInput(select, false);
  if (!isStandard) {
    const input = otherInputFor(select);
    if (input) input.value = value || '';
  }
}

// Switches the "Add New Entry" form into editing an existing record: fills
// every field from what's already loaded client-side (no extra round trip),
// swaps the header/button wording, and scrolls the form into view.
function beginEdit(recordId) {
  const data = entries.find(e => String(e.recordId) === String(recordId));
  if (!data) return;

  editingRecordId = data.recordId;
  form.elements['code'].value = data.code;
  form.elements['title'].value = data.title;
  form.elements['location'].value = data.location;
  // Older records can hold free-text Period Covered ("2023-2024") that the
  // date pickers can't show — only a "yyyy-MM-dd to yyyy-MM-dd" range (what
  // the pickers themselves produce) is recognized; anything else leaves
  // both pickers blank, forcing the dates to be set once on edit.
  const periodMatch = /^(\d{4}-\d{2}-\d{2}) to (\d{4}-\d{2}-\d{2})$/.exec(data.periodCovered);
  form.elements['periodFrom'].value = periodMatch ? periodMatch[1] : '';
  form.elements['periodTo'].value = periodMatch ? periodMatch[2] : '';
  // retentionPeriod is the due date itself for records logged with the date
  // picker (yyyy-MM-dd, what the <input type="date"> needs). Older records
  // still hold a free-text duration there ("5 Years") that the date input
  // can't show, so fall back to the already-computed due date — editing an
  // old record now also moves it onto the date-picker scheme.
  form.elements['retention'].value = /^\d{4}-\d{2}-\d{2}$/.test(data.retentionPeriod)
    ? data.retentionPeriod
    : (data.retentionDueDate || '');
  setChoiceField(form.elements['medium'], data.medium);
  setChoiceField(form.elements['filing'], data.filingSystem);
  setChoiceField(form.elements['access'], data.accessControl);

  document.getElementById('entry-form-title').textContent = 'Edit Entry';
  document.getElementById('entry-form-desc').textContent = `Correcting ${data.code}`;
  document.getElementById('entry-form-badge').textContent = 'EDITING';
  const submitBtn = document.getElementById('entry-form-submit-btn');
  submitBtn.innerHTML = '<span class="material-symbols-outlined">save</span> Update Entry';
  document.getElementById('entry-form-cancel-btn').hidden = false;

  expandEntryForm();
  form.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function cancelEdit() {
  editingRecordId = null;
  form.reset();
  document.getElementById('entry-form-title').textContent = 'Add New Entry';
  document.getElementById('entry-form-desc').textContent = 'Fill in the details to archive a new document';
  document.getElementById('entry-form-badge').textContent = 'NEW RECORD';
  document.getElementById('entry-form-submit-btn').innerHTML = '<span class="material-symbols-outlined">save</span> Save Entry';
  document.getElementById('entry-form-cancel-btn').hidden = true;
}

const cancelEditBtn = document.getElementById('entry-form-cancel-btn');
if (cancelEditBtn) {
  cancelEditBtn.addEventListener('click', cancelEdit);
}

// Add New Entry: collapsed by default (searching/reviewing records is the
// far more common reason to be on this page than adding one).
const entryToggleBtn = document.getElementById('entry-toggle-btn');
const entryFormBody = document.getElementById('entry-form-body');

function expandEntryForm() {
  if (!entryToggleBtn || !entryFormBody) return;
  entryToggleBtn.setAttribute('aria-expanded', 'true');
  entryFormBody.hidden = false;
}

function collapseEntryForm() {
  if (!entryToggleBtn || !entryFormBody) return;
  entryToggleBtn.setAttribute('aria-expanded', 'false');
  entryFormBody.hidden = true;
}

if (entryToggleBtn) {
  entryToggleBtn.addEventListener('click', () => {
    if (entryFormBody.hidden) {
      expandEntryForm();
    } else {
      collapseEntryForm();
    }
  });
}

async function handleDelete(btn) {
  const confirmed = await showNotice({
    tone: 'error',
    icon: 'warning',
    title: 'Delete this record?',
    file: btn.dataset.code,
    message: 'This permanently removes the record. This cannot be undone.',
    okText: 'Delete',
    cancelText: 'Cancel'
  });
  if (!confirmed) return;

  btn.disabled = true;
  try {
    await postAction(`/Records/Delete?id=${encodeURIComponent(btn.dataset.id)}`);
    if (editingRecordId != null && String(editingRecordId) === String(btn.dataset.id)) {
      cancelEdit();
    }
    await loadRecords();
  } catch (err) {
    btn.disabled = false;
    await showNotice({
      tone: 'error',
      icon: 'error',
      title: 'Couldn’t delete the record',
      message: err.message || 'Please try again.',
      okText: 'OK'
    });
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
  const periodFromInput = form.elements['periodFrom'];
  const periodToInput = form.elements['periodTo'];
  if (periodToInput.value < periodFromInput.value) {
    periodToInput.setCustomValidity('The end date must be on or after the start date.');
    periodToInput.reportValidity();
    periodToInput.addEventListener('input', () => periodToInput.setCustomValidity(''), { once: true });
    return;
  }

  const btn = form.querySelector('button[type="submit"]');
  const originalContent = btn.innerHTML;
  const isEditing = editingRecordId != null;
  btn.innerHTML = isEditing
    ? `<span class="material-symbols-outlined animate-spin">sync</span> Updating...`
    : `<span class="material-symbols-outlined animate-spin">sync</span> Recording...`;
  btn.disabled = true;

  const formData = new FormData(form);
  const payload = {
    code: formData.get('code').trim(),
    title: formData.get('title').trim(),
    medium: chosenValue(form.elements['medium']),
    location: formData.get('location').trim(),
    periodCovered: `${periodFromInput.value} to ${periodToInput.value}`,
    filingSystem: chosenValue(form.elements['filing']),
    accessControl: chosenValue(form.elements['access']),
    retentionPeriod: formData.get('retention').trim()
  };
  if (isEditing) {
    payload.recordId = editingRecordId;
  }

  try {
    const res = await fetch(isEditing ? '/Records/Update' : '/Records/Save', {
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

    btn.innerHTML = `<span class="material-symbols-outlined">check_circle</span> Success!`;

    if (isEditing) {
      await loadRecords();
    } else {
      const saved = await res.json();
      entries.push(saved);
      // Jump to the page that now contains the new record
      currentPage = totalPages();
      render();
    }

    setTimeout(() => {
      btn.disabled = false;
      if (isEditing) {
        cancelEdit(); // also restores the button's own label
      } else {
        btn.innerHTML = originalContent;
        form.reset();
      }
    }, 1200);

  } catch (err) {
    // fetch() throws a TypeError when offline. Rather than lose what was
    // typed, queue it — offline-sync.js replays it as a normal
    // /Records/Save call once the connection returns, since a new record
    // never conflicts with anyone else's (owner-scoped, no shared state).
    // Editing isn't queued offline: it depends on server-side state
    // (ownership, masterlist status) that can't be safely assumed stale.
    if (!isEditing && err instanceof TypeError && window.DTIOffline) {
      window.DTIOffline.queueRecord(payload);
      btn.innerHTML = `<span class="material-symbols-outlined">pending</span> Saved offline`;
      setTimeout(() => {
        btn.innerHTML = originalContent;
        btn.disabled = false;
        form.reset();
      }, 1500);
      return;
    }

    // The button is too small for a full sentence, so it shows a short
    // status and the full reason appears in a dialog that stays until
    // the user closes it.
    btn.innerHTML = `<span class="material-symbols-outlined">error</span> Not saved`;
    showError(isEditing ? 'Record not updated' : 'Record not saved', errorMessage(err));
    setTimeout(() => {
      btn.innerHTML = originalContent;
      btn.disabled = false;
    }, 1500);
  }
});

// A queued record synced successfully in the background (offline-sync.js) —
// reload the list so it appears without the user having to refresh manually.
window.addEventListener('dti-offline-record-synced', () => {
  loadRecords();
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

// ---------- Import Masterlist ----------
// Lets a user upload a masterlist .xlsx file (in the same layout this system
// exports) and have its rows become real records, instead of retyping each
// one by hand. Every row goes through the exact same validation as the
// single-entry form (TryInsertRecord server-side) — bulk data gets no looser
// or stricter rules than manual entry.
const importMasterlistBtn = document.getElementById('import-masterlist-btn');
const importMasterlistInput = document.getElementById('import-masterlist-input');

if (importMasterlistBtn && importMasterlistInput) {
  importMasterlistBtn.addEventListener('click', () => importMasterlistInput.click());

  importMasterlistInput.addEventListener('change', async () => {
    const file = importMasterlistInput.files[0];
    if (!file) return;

    const originalContent = importMasterlistBtn.innerHTML;
    importMasterlistBtn.innerHTML = '<span class="material-symbols-outlined animate-spin">sync</span> Importing...';
    importMasterlistBtn.disabled = true;

    const formData = new FormData();
    formData.append('file', file);

    try {
      const res = await fetch('/Records/ImportMasterlist', {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': getCsrfToken() },
        body: formData
      });

      if (!res.ok) {
        throw new Error(await readErrorMessage(res));
      }

      const result = await res.json();
      entries.push(...result.imported);
      currentPage = totalPages();
      render();

      const skippedSteps = (result.skipped || []).map(s => `Row ${s.row}: ${s.reason}`);
      await showNotice({
        tone: result.truncated || (result.skipped && result.skipped.length) ? 'warning' : 'success',
        icon: result.truncated || (result.skipped && result.skipped.length) ? 'warning' : 'check_circle',
        title: 'Import complete',
        message: (result.truncated ? `${result.message} ` : '') +
          `Imported ${result.imported.length} record${result.imported.length === 1 ? '' : 's'}.` +
          (skippedSteps.length ? ` ${skippedSteps.length} row${skippedSteps.length === 1 ? '' : 's'} were skipped:` : ''),
        steps: skippedSteps
      });
    } catch (err) {
      showError('Import not completed', errorMessage(err));
    } finally {
      importMasterlistBtn.innerHTML = originalContent;
      importMasterlistBtn.disabled = false;
      importMasterlistInput.value = '';
    }
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
           <span class="material-symbols-outlined">edit_note</span> Add Records
         </button>`;
    li.innerHTML = `
      <div class="masterlist-info">
        <span class="xlsx-chip"><span class="material-symbols-outlined" aria-hidden="true">description</span>XLSX</span>
        <div>
          <div class="masterlist-name" data-name-text>${escapeHtml(item.fileName)}${item.isOpen ? '<span class="masterlist-open-tag">Adding records</span>' : ''}</div>
          <div class="masterlist-meta">Last saved ${escapeHtml(item.savedAt)} · ${item.recordCount} ${recordWord}</div>
        </div>
      </div>
      <div class="masterlist-actions">
        <button class="btn btn-icon" type="button" data-rename="${item.id}" aria-label="Rename ${escapeHtml(item.fileName)}" title="Rename">
          <span class="material-symbols-outlined">edit</span>
        </button>
        <button class="btn btn-icon" type="button" data-print="${item.id}" aria-label="Print ${escapeHtml(item.fileName)}" title="Print">
          <span class="material-symbols-outlined">print</span>
        </button>
        <div class="actions-divider" aria-hidden="true"></div>
        ${addButton}
        <a class="btn btn-primary" href="/Records/DownloadMasterlist?id=${encodeURIComponent(item.id)}" data-download="${item.id}">
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