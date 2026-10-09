// Dynamic add/remove text rows for the Subtasks field on SuperAdmin's
// Create Main Task form. Each input shares name="SubtaskNames" so ASP.NET
// Core model-binds them into TaskCreateViewModel.SubtaskNames automatically
// — no indexing needed. Only present on the page when
// ViewBag.CanCreateMainTask is true.
(function () {
    'use strict';

    // The first row is permanent: it is built without a remove button, so
    // there is always at least one subtask box. Only added rows can be removed.
    function createRow(removable) {
        var row = document.createElement('div');
        row.className = 'subtask-row';

        var input = document.createElement('input');
        input.type = 'text';
        input.name = 'SubtaskNames';
        input.className = 'pill-input';
        input.setAttribute('data-capitalize', 'title'); // text-format.js
        input.placeholder = 'e.g. Prepare venue logistics';
        row.appendChild(input);

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
            if (!row.classList.contains('subtask-row-first')) row.remove();
        });
        row.appendChild(removeBtn);
        return row;
    }

    function init() {
        var list = document.getElementById('subtaskList');
        var addBtn = document.getElementById('addSubtaskBtn');
        if (!list || !addBtn) return; // absent for Admin/Supervisor pages

        list.appendChild(createRow(false)); // the permanent first row

        addBtn.addEventListener('click', function () {
            list.appendChild(createRow(true));
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();