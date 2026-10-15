// ==========================================================================
// Review Submission — overlay modal (same pattern as tasks-create.js) plus
// a confirm before Approve or Return, the same way Delete Task and Stop
// Repeating already do. Approve marks the task Completed (no way back from
// here); Return sends it back to the person who submitted it, and only
// proceeds once Admin remarks are filled in (the message makes that
// requirement visible before the server rejects it).
// ==========================================================================
(function () {
    var modal = document.getElementById('reviewModal');
    var form = document.getElementById('trForm');

    // ── Open: a native modal dialog sits above everything and dims the
    // real Task Management list behind it, same as Create Task. ──
    if (modal && typeof modal.showModal === 'function') {
        modal.showModal();
    } else if (modal) {
        modal.setAttribute('open', ''); // very old browsers: shown, not modal
    }

    // ── Close: animate out, then back to Task Management ──
    function closeModal() {
        if (!modal) return;
        var box = modal.querySelector('.modal');
        if (box) box.classList.add('closing');
        setTimeout(function () {
            window.location.href = modal.dataset.closeUrl || '/Tasks';
        }, 150);
    }

    if (!form) {
        return;
    }

    var taskName = form.getAttribute('data-task-name') || 'this task';
    var submittedBy = form.getAttribute('data-submitted-by') || 'this person';
    var remarksField = form.querySelector('textarea[name="AdminRemarks"]');

    // ── Close on a click outside the form, unless remarks have been typed —
    // same "don't throw away what they wrote" rule as Create Task. ──
    var touched = !!document.querySelector('#reviewModal .tr-error-text:not(:empty)');
    remarksField && remarksField.addEventListener('input', function () { touched = true; });

    if (modal) {
        modal.addEventListener('click', function (e) {
            if (e.target === modal && !touched) closeModal();
        });

        // ── Close on Escape (the browser fires "cancel") ──
        modal.addEventListener('cancel', function (e) {
            e.preventDefault();
            closeModal();
        });
    }

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
})();
