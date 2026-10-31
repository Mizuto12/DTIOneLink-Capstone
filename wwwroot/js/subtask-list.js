// Dynamic add/remove rows for the Subtasks field on SuperAdmin's Create
// Main Task form. Each name input shares name="SubtaskNames" and each
// weight input shares name="SubtaskWeights" so ASP.NET Core model-binds
// them into TaskCreateViewModel.SubtaskNames/SubtaskWeights by position —
// no indexing needed. Only present on the page when ViewBag.CanCreateMainTask
// is true.
//
// Weight is a literal percentage of the whole directive's progress (not a
// relative share) — TasksController rejects a submission whose subtasks sum
// past 100. A new row is prefilled with whatever's still unallocated rather
// than redistributing everyone evenly, so adjusting one subtask's share
// doesn't silently change numbers the OPD already typed into the others.
(function () {
    'use strict';

    var MAX_TOTAL = 100;

    function currentTotal(list, excludeInput) {
        var total = 0;
        list.querySelectorAll('.subtask-weight-input').forEach(function (input) {
            if (input === excludeInput) return;
            total += Number(input.value) || 0;
        });
        return total;
    }

    function updateTotalReadout(list, totalEl) {
        if (!totalEl) return;
        var total = currentTotal(list, null);
        totalEl.textContent = 'Total allocated: ' + total + '% / ' + MAX_TOTAL + '%';
        totalEl.classList.toggle('is-over', total > MAX_TOTAL);
    }

    // The first row is permanent: it is built without a remove button, so
    // there is always at least one subtask box. Only added rows can be removed.
    function createRow(removable, list, totalEl) {
        var row = document.createElement('div');
        row.className = 'subtask-row';

        var input = document.createElement('input');
        input.type = 'text';
        input.name = 'SubtaskNames';
        input.className = 'pill-input';
        input.setAttribute('data-capitalize', 'title'); // text-format.js
        input.placeholder = 'e.g. Prepare venue logistics';
        row.appendChild(input);

        var weightGroup = document.createElement('div');
        weightGroup.className = 'subtask-weight-group';

        var weightInput = document.createElement('input');
        weightInput.type = 'number';
        weightInput.name = 'SubtaskWeights';
        weightInput.className = 'subtask-weight-input';
        weightInput.min = '1';
        weightInput.max = String(MAX_TOTAL);
        weightInput.step = '1';
        // Prefilled with whatever share of 100% isn't already claimed by
        // the other rows, floored at 1 — not an even redistribution, so
        // typing into one row never silently changes another.
        var remaining = Math.max(1, MAX_TOTAL - currentTotal(list, null));
        weightInput.value = String(remaining);
        weightInput.title = 'What share of this directive\'s overall progress this subtask is worth. All subtasks together can\'t add up to more than 100%.';
        weightInput.setAttribute('aria-label', 'Percent of main task progress');
        weightInput.addEventListener('input', function () { updateTotalReadout(list, totalEl); });
        weightGroup.appendChild(weightInput);

        var percentSign = document.createElement('span');
        percentSign.className = 'subtask-weight-percent';
        percentSign.textContent = '%';
        percentSign.setAttribute('aria-hidden', 'true');
        weightGroup.appendChild(percentSign);

        row.appendChild(weightGroup);

        if (!removable) {
            row.classList.add('subtask-row-first');
            // Empty space the size of the remove button, so every box lines up.
            var spacer = document.createElement('span');
            spacer.className = 'subtask-remove-spacer';
            spacer.setAttribute('aria-hidden', 'true');
            row.appendChild(spacer);
            return row;
        }

        var removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'subtask-remove-btn';
        removeBtn.setAttribute('aria-label', 'Remove subtask');
        removeBtn.innerHTML = '<span class="material-symbols-outlined">close</span>';
        removeBtn.addEventListener('click', function () {
            if (!row.classList.contains('subtask-row-first')) {
                row.remove();
                updateTotalReadout(list, totalEl);
            }
        });
        row.appendChild(removeBtn);
        return row;
    }

    function init() {
        var list = document.getElementById('subtaskList');
        var addBtn = document.getElementById('addSubtaskBtn');
        if (!list || !addBtn) return; // absent for Admin/Supervisor pages

        var totalEl = document.getElementById('subtaskWeightTotal');

        list.appendChild(createRow(false, list, totalEl)); // the permanent first row
        updateTotalReadout(list, totalEl);

        addBtn.addEventListener('click', function () {
            list.appendChild(createRow(true, list, totalEl));
            updateTotalReadout(list, totalEl);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
