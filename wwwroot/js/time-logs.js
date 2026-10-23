// Time Logs: expands/collapses a day card's session list past its capped
// view (see Index.cshtml — sessions beyond the cap render with `hidden`
// already set server-side, so there's no layout jump before this runs).
// Same show-more/show-less pattern as SuperAdmin's Task Board columns.
(function () {
  document.querySelectorAll('[data-day-card]').forEach(function (card) {
    var toggle = card.querySelector('[data-show-more]');
    if (!toggle) return;

    var extraRows = Array.prototype.slice.call(card.querySelectorAll('[data-extra-session]'));
    var label = toggle.querySelector('[data-show-more-label]');
    var collapsedText = label.textContent;
    var expandedText = 'Show less';

    toggle.addEventListener('click', function () {
      var expanding = extraRows.some(function (row) { return row.hidden; });
      extraRows.forEach(function (row) { row.hidden = !expanding; });
      label.textContent = expanding ? expandedText : collapsedText;
      toggle.classList.toggle('is-expanded', expanding);
    });
  });

  // Daily Summary: show/hide, plus a client-side CSV export of exactly the
  // rows currently in the table — same pattern as Reports' Export Data.
  var summaryToggle = document.getElementById('toggle-daily-summary');
  var summarySection = document.getElementById('daily-summary');
  if (summaryToggle && summarySection) {
    summaryToggle.addEventListener('click', function () {
      var show = summarySection.hidden;
      summarySection.hidden = !show;
      summaryToggle.setAttribute('aria-expanded', String(show));
      summaryToggle.classList.toggle('is-active', show);
    });
  }

  function csvCell(value) {
    var s = String(value == null ? '' : value);
    return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  }

  var exportBtn = document.getElementById('export-daily-summary');
  if (exportBtn) {
    exportBtn.addEventListener('click', function () {
      var rows = Array.prototype.slice.call(document.querySelectorAll('#daily-summary tbody tr'));
      var lines = [['Date', 'Earliest Time In', 'Latest Time Out', 'Total Hours Logged']]
        .concat(rows.map(function (row) {
          return [
            row.dataset.date,
            row.dataset.earliestIn,
            row.dataset.latestOut || 'Still signed in',
            row.dataset.hours
          ];
        }))
        .map(function (cols) { return cols.map(csvCell).join(','); });
      var blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
      var today = new Date().toISOString().slice(0, 10);
      var link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = 'onelink-time-logs-daily-summary-' + today + '.csv';
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(function () { URL.revokeObjectURL(link.href); }, 1000);
    });
  }
})();
