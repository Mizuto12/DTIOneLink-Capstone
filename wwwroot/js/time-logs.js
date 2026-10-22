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
})();
