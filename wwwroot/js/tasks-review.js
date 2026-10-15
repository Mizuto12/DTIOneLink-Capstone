// ==========================================================================
// Review Submission — confirm before Approve or Return, the same way
// Delete Task and Stop Repeating already do. Approve marks the task
// Completed (no way back from here); Return sends it back to the person
// who submitted it, and only proceeds once Admin remarks are filled in
// (the message makes that requirement visible before the server rejects it).
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    var form = document.getElementById('trForm');
    if (!form) {
        return;
    }

    var taskName = form.getAttribute('data-task-name') || 'this task';
    var submittedBy = form.getAttribute('data-submitted-by') || 'this person';
    var remarksField = form.querySelector('textarea[name="AdminRemarks"]');

    form.addEventListener('submit', function (event) {
        var decision = event.submitter ? event.submitter.value : null;
        if (decision !== 'approve' && decision !== 'return') {
            return; // unknown trigger (e.g. Enter key with no submitter) — let the server validate as before
        }

        var message;
        if (decision === 'approve') {
            message = 'Approve "' + taskName + '" submitted by ' + submittedBy + '?\n\n' +
                'This marks the task Completed. This cannot be undone.';
        } else {
            var remarks = remarksField ? remarksField.value.trim() : '';
            if (!remarks) {
                // Confirming first would be misleading if the server is just
                // going to reject it anyway for a missing required field.
                return;
            }
            message = 'Return "' + taskName + '" to ' + submittedBy + ' for correction?\n\n' +
                submittedBy + ' will see your remarks and need to resubmit.';
        }

        if (!window.confirm(message)) {
            event.preventDefault();
        }
    });
});
