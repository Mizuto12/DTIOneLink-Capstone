// Dynamic add/remove text rows for the Subtasks field on SuperAdmin's
// Create Main Task form. Each input shares name="SubtaskNames" so ASP.NET
// Core model-binds them into TaskCreateViewModel.SubtaskNames automatically
// — no indexing needed. Only present on the page when
// ViewBag.CanCreateMainTask is true.
(function () {
    'use strict';

    function createRow() {
        var row = document.createElement('div');
        row.className = 'subtask-row';

        var input = document.createElement('input');
        input.type = 'text';
        input.name = 'SubtaskNames';
        input.className = 'pill-input';
        input.placeholder = 'e.g. Prepare venue logistics';

        var removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'subtask-remove-btn';
        removeBtn.setAttribute('aria-label', 'Remove subtask');
        removeBtn.innerHTML = '<span class="material-symbols-outlined">close</span>';
        removeBtn.addEventListener('click', function () {
            row.remove();
        });

        row.appendChild(input);
        row.appendChild(removeBtn);
        return row;
    }

    function init() {
        var list = document.getElementById('subtaskList');
        var addBtn = document.getElementById('addSubtaskBtn');
        if (!list || !addBtn) return; // absent for Admin/Supervisor pages

        list.appendChild(createRow()); // start with one empty row

        addBtn.addEventListener('click', function () {
            list.appendChild(createRow());
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();