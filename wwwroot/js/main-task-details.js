// ==========================================================================
// Main Task Details — confirms before Delete, Stop Repeating, and
// reassigning the Responsible Admin (OPD only) all go through the same
// styled dialog (notice-dialog.js) instead of the browser's own
// confirm() popup, which read as broken/unfinished at exactly the
// highest-stakes moments on this page.
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    confirmBeforeSubmit('mtdDeleteForm', function (form) {
        var hasSubtasks = form.getAttribute('data-has-subtasks') === 'true';
        return {
            tone: 'danger',
            icon: 'delete',
            title: 'Delete this task permanently?',
            message: 'This cannot be undone.' + (hasSubtasks ? ' Every subtask under it will be deleted too.' : ''),
            okText: 'Delete',
            cancelText: 'Cancel'
        };
    });

    confirmBeforeSubmit('mtdStopRepeatForm', function () {
        return {
            tone: 'warning',
            icon: 'event_busy',
            title: 'Stop repeating this task?',
            message: 'No more copies will be sent out. Copies already sent are not affected.',
            okText: 'Stop repeating',
            cancelText: 'Cancel'
        };
    });

    var reassignForm = document.getElementById('mtdReassignForm');
    var reassignSelect = document.getElementById('mtdReassignSelect');
    if (reassignForm && reassignSelect) {
        var taskName = reassignForm.getAttribute('data-task-name') || 'this task';

        reassignForm.addEventListener('submit', function (event) {
            if (reassignForm.dataset.confirmed === 'true') {
                return;
            }
            event.preventDefault();

            var chosenOption = reassignSelect.options[reassignSelect.selectedIndex];
            var adminName = chosenOption ? chosenOption.textContent.trim() : 'this Admin';

            window.showNotice({
                tone: 'warning',
                icon: 'swap_horiz',
                title: 'Reassign "' + taskName + '" to ' + adminName + '?',
                message: 'They will be notified and will become responsible for this task.',
                okText: 'Reassign',
                cancelText: 'Cancel'
            }).then(function (ok) {
                if (ok) {
                    reassignForm.dataset.confirmed = 'true';
                    reassignForm.requestSubmit ? reassignForm.requestSubmit() : reassignForm.submit();
                }
            });
        });
    }

    // Shared helper: intercepts a form's submit, shows the dialog built by
    // optionsBuilder(form), and resubmits for real only if confirmed. A
    // "confirmed" flag avoids re-showing the dialog on the real resubmit.
    function confirmBeforeSubmit(formId, optionsBuilder) {
        var form = document.getElementById(formId);
        if (!form) {
            return;
        }

        form.addEventListener('submit', function (event) {
            if (form.dataset.confirmed === 'true') {
                return;
            }
            event.preventDefault();

            window.showNotice(optionsBuilder(form)).then(function (ok) {
                if (ok) {
                    form.dataset.confirmed = 'true';
                    form.requestSubmit ? form.requestSubmit() : form.submit();
                }
            });
        });
    }
});
