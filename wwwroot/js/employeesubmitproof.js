// ==========================================================================
// Submit Proof of Completion — fetch-based submission with an offline
// fallback. Online, this behaves like the original full-page form POST
// (same redirect on success). Offline, the file and remarks are queued via
// offline-sync.js and replayed automatically once the connection returns.
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    var form = document.getElementById('tpForm');
    if (!form) {
        return;
    }

    var submitBtn = document.getElementById('tpSubmitBtn');
    var hint = document.getElementById('tpStatusHint');
    var fileInput = document.getElementById('tpFile');
    var remarksInput = document.getElementById('tpRemarks');
    var taskId = Number(form.getAttribute('data-task-id'));

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
                if (res.redirected || res.ok) {
                    // The controller redirects to Details on success.
                    window.location.href = res.redirected ? res.url : ('/Employee/Details/' + taskId);
                    return;
                }
                return res.text().then(function () {
                    submitBtn.disabled = false;
                    setHint('Unable to submit — the task may have changed. Please refresh and try again.', 'error');
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
