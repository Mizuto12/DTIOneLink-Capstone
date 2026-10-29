// ==========================================================================
// Submit Proof of Completion — overlay modal (same pattern as
// tasks-create.js, but with no real page behind it — just the dialog's own
// dim backdrop) plus fetch-based submission with an offline fallback.
// Online, this behaves like the original full-page form POST (same
// redirect on success). Offline, the file and remarks are queued via
// offline-sync.js and replayed automatically once the connection returns.
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    var modal = document.getElementById('submitProofModal');
    var form = document.getElementById('tpForm');

    if (modal && typeof modal.showModal === 'function') {
        modal.showModal();
    } else if (modal) {
        modal.setAttribute('open', '');
    }

    // This page is a real, separately-navigable URL (not a true client-side
    // modal) rendering a dialog over an inert copy of Details — opening it
    // always adds its own history entry. Every way of leaving it used to
    // navigate FORWARD to a fresh Details entry instead of consuming the
    // one already behind it, so the stack only ever grew: Details ->
    // SubmitProof -> Details(new). One Back press from there landed back
    // on this same SubmitProof page instead of wherever the user actually
    // came from. history.back() instead collapses it the way a real close
    // should. Falls back to a plain navigation only when there's nothing
    // of ours to go back to (direct link, new tab, bookmark) — history.back()
    // in that case could leave the app entirely.
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
                window.location.href = modal.dataset.closeUrl || '/Employee';
            }
        }, 150);
    }

    var closeBtn = document.getElementById('tpCloseBtn');
    var cancelBtn = document.getElementById('tpCancelBtn');
    [closeBtn, cancelBtn].forEach(function (btn) {
        if (!btn) return;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            closeModal();
        });
    });

    if (!form) {
        return;
    }

    var submitBtn = document.getElementById('tpSubmitBtn');
    var hint = document.getElementById('tpStatusHint');
    var fileInput = document.getElementById('tpFile');
    var remarksInput = document.getElementById('tpRemarks');
    var taskId = Number(form.getAttribute('data-task-id'));

    // ── Close on a click outside the form, unless something's been picked
    // or typed — same "don't throw away what they started" rule as
    // Create Task. ──
    var touched = false;
    fileInput.addEventListener('change', function () { touched = true; });
    remarksInput.addEventListener('input', function () { touched = true; });

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

    function setHint(text, state) {
        if (!hint) {
            return;
        }
        hint.textContent = text;
        hint.classList.toggle('is-saved', state === 'saved');
        hint.classList.toggle('is-error', state === 'error');
    }

    function getCsrfToken() {
        var input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    form.addEventListener('submit', function (event) {
        event.preventDefault();

        if (!fileInput.files[0]) {
            setHint('Please attach a file as proof of completion.', 'error');
            return;
        }

        submitBtn.disabled = true;
        setHint('Submitting…', null);

        var body = new FormData();
        body.append('Id', String(taskId));
        body.append('ProofFile', fileInput.files[0]);
        body.append('Remarks', remarksInput.value || '');
        body.append('__RequestVerificationToken', getCsrfToken());

        fetch('/Employee/SubmitProof', { method: 'POST', body: body })
            .then(function (res) {
                // Only an actual redirect means the server accepted the
                // submission — that's the ONLY response SubmitProof's POST
                // action sends on success. A rejected file (wrong type,
                // too large, content/extension mismatch) or any other
                // ModelState error re-renders this same modal at a plain
                // 200 OK, which used to be treated as success here too —
                // silently discarding the error and bouncing the user back
                // to Details with nothing actually submitted.
                if (res.redirected) {
                    // replace(), not href=: this overwrites the SubmitProof
                    // entry in place with the post-submit Details page
                    // instead of stacking a new entry on top of it — see
                    // closeModal()'s comment for why that matters for Back.
                    window.location.replace(res.url);
                    return;
                }
                return res.text().then(function (html) {
                    submitBtn.disabled = false;
                    var errors = [];
                    try {
                        var doc = new DOMParser().parseFromString(html, 'text/html');
                        errors = Array.prototype.map.call(
                            doc.querySelectorAll('.tp-alert.error li'),
                            function (li) { return li.textContent.trim(); }
                        ).filter(Boolean);
                    } catch (e) { /* fall through to the generic message below */ }
                    setHint(errors.length ? errors.join(' ') : 'Unable to submit — the task may have changed. Please refresh and try again.', 'error');
                });
            })
            .catch(function (err) {
                // fetch() throws a TypeError when the server can't be reached —
                // queue it instead of losing the file and remarks.
                if (err instanceof TypeError && window.DTIOffline) {
                    window.DTIOffline.queueProof({
                        taskId: taskId,
                        remarks: remarksInput.value || '',
                        file: fileInput.files[0]
                    }).then(function () {
                        submitBtn.disabled = false;
                        setHint('Saved offline — this will submit automatically once you\'re back online.', 'saved');
                    });
                    return;
                }
                submitBtn.disabled = false;
                setHint('Unable to reach the server. Please check your connection and try again.', 'error');
            });
    });
});
