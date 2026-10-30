// Remove-checklist-item confirmation — was a raw window.confirm() (the same
// inconsistency already fixed on the Employee side's delete confirmation),
// swapped to the app's styled notice-dialog.js, same pattern as
// main-task-details.js's reassign/stop-repeating/delete confirmations.
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.checklist-page__remove-form').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (form.dataset.confirmed === 'true') {
                return;
            }
            event.preventDefault();

            var label = form.getAttribute('data-item-label') || 'this item';
            window.showNotice({
                tone: 'danger',
                icon: 'delete',
                title: 'Remove "' + label + '"?',
                message: 'This checklist item will be removed from this assignee\'s checklist.',
                okText: 'Remove',
                cancelText: 'Cancel'
            }).then(function (ok) {
                if (ok) {
                    form.dataset.confirmed = 'true';
                    form.requestSubmit ? form.requestSubmit() : form.submit();
                }
            });
        });
    });
});
