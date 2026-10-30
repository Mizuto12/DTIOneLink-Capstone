// ==========================================================================
// Review Submission — overlay modal (same pattern as tasks-create.js) plus
// a styled confirm (notice-dialog.js) before Approve or Return, the same
// one Delete Task and Stop Repeating already use instead of the browser's
// own confirm() popup. Approve marks the task Completed (no way back from
// here); Return sends it back to the person who submitted it, and only
// shows the confirm once Admin remarks are filled in — confirming first
// would be misleading if the server is just going to reject it anyway for
// a missing required field.
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
    // This page is a real, separately-navigable URL (not a true client-side
    // modal) rendering a dialog over an inert copy of Task Management —
    // opening it always adds its own history entry, same as SubmitProof did
    // before its fix. A plain forward navigation here means the stack only
    // ever grows: TaskManagement -> Review -> TaskManagement(new); one Back
    // press from there lands back on this same Review page instead of
    // wherever the user actually came from. history.back() instead
    // collapses it the way a real close should — same fix as
    // employeesubmitproof.js's closeModal(). Falls back to a plain
    // navigation only when there's nothing of ours to go back to (direct
    // link, new tab, bookmark), where history.back() could leave the app.
    function closeModal() {
        if (!modal) return;
        var box = modal.querySelector('.modal');
        if (box) box.classList.add('closing');
        setTimeout(function () {
            var cameFromThisApp = document.referrer &&
                document.referrer.indexOf(window.location.origin + '/') === 0;
            if (cameFromThisApp && window.history.length > 1) {
                window.history.back();
            } else {
                window.location.href = modal.dataset.closeUrl || '/Tasks';
            }
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

    // Which decision last got a real, confirmed submit — a hidden field
    // named "Decision" re-sends it on the real resubmit, since the trigger
    // button that originally sent it is no longer the submitter once the
    // form is resubmitted programmatically via requestSubmit().
    var confirmedDecision = null;

    form.addEventListener('submit', function (event) {
        if (confirmedDecision) {
            return; // the real resubmit after confirming — let it go through
        }

        var decision = event.submitter ? event.submitter.value : null;
        if (decision !== 'approve' && decision !== 'return') {
            return; // unknown trigger (e.g. Enter key with no submitter) — let the server validate as before
        }

        event.preventDefault();

        var options;
        if (decision === 'approve') {
            options = {
                tone: 'warning',
                icon: 'check_circle',
                title: 'Approve "' + taskName + '" submitted by ' + submittedBy + '?',
                message: 'This marks the task Completed. This cannot be undone.',
                okText: 'Approve',
                cancelText: 'Cancel'
            };
        } else {
            var remarks = remarksField ? remarksField.value.trim() : '';
            if (!remarks) {
                // Letting the real submit through here (instead of showing
                // the confirm) sends the user straight to the server-side
                // "Remarks are required" error — confirming first would be
                // misleading if the server is just going to reject it anyway.
                // form.submit() doesn't carry the trigger button's
                // name/value, so the decision is added as a hidden field,
                // same as the real resubmit path below.
                confirmedDecision = decision;
                var decisionField = document.createElement('input');
                decisionField.type = 'hidden';
                decisionField.name = 'Decision';
                decisionField.value = decision;
                form.appendChild(decisionField);
                form.submit();
                return;
            }
            options = {
                tone: 'warning',
                icon: 'undo',
                title: 'Return "' + taskName + '" to ' + submittedBy + ' for correction?',
                message: submittedBy + ' will see your remarks and need to resubmit.',
                okText: 'Return',
                cancelText: 'Cancel'
            };
        }

        window.showNotice(options).then(function (ok) {
            if (!ok) {
                return;
            }
            confirmedDecision = decision;
            // The trigger button is what the server reads as the chosen
            // decision (name="Decision" value="approve"/"return") — a plain
            // form.submit() wouldn't include it, so add it as a hidden field
            // on the real resubmit instead.
            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'Decision';
            hidden.value = decision;
            form.appendChild(hidden);
            form.requestSubmit ? form.requestSubmit() : form.submit();
        });
    });
})();
