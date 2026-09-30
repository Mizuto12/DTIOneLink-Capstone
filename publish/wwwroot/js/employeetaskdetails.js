// ==========================================================================
// Laguna Governance – Task Details page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initCopyTaskCode();
    initBackButton();
    initProgressSlider();
    initDeleteConfirm();
});

/**
 * The Back button uses browser history rather than a fixed link, since
 * this page can now be reached from either the Employee Kanban board
 * or the Admin/Employee dashboard's Gantt/Kanban views.
 */
function initBackButton() {
    var btn = document.getElementById('tdBackButton');

    if (!btn) {
        return;
    }

    btn.addEventListener('click', function () {
        window.history.back();
    });
}

/**
 * Clicking the TASK-#### code copies it to the clipboard and briefly
 * confirms the copy in the button's own label.
 */
function initCopyTaskCode() {
    var btn = document.getElementById('copyTaskCode');

    if (!btn) {
        return;
    }

    var originalLabel = btn.textContent;

    btn.addEventListener('click', function () {
        var code = btn.getAttribute('data-code') || originalLabel.trim();

        if (!navigator.clipboard) {
            return;
        }

        navigator.clipboard.writeText(code).then(function () {
            btn.textContent = 'Copied!';
            setTimeout(function () {
                btn.textContent = originalLabel;
            }, 1200);
        }).catch(function () {
            // Clipboard write failed (e.g. permissions) — fail silently,
            // the code is still visible in the button label.
        });
    });
}
/**
 * Progress slider (assignee only). Dragging updates the number shown; letting
 * go saves to /Employee/UpdateProgress. The server applies the same rules as
 * the old Update Progress page; if it refuses, the slider returns to the
 * last saved value and the reason is shown under it.
 */
function initProgressSlider() {
    var range = document.getElementById('tdProgressRange');
    if (!range) {
        return;
    }

    var valueLabel = document.getElementById('tdProgressValue');
    var hint = document.getElementById('tdProgressHint');
    var metaProgress = document.getElementById('tdMetaProgress');
    var metaStatus = document.getElementById('tdMetaStatus');
    var badgeText = document.getElementById('tdStatusBadgeText');
    var badge = document.getElementById('tdStatusBadge');
    var taskId = range.getAttribute('data-task-id');
    var isOverdue = range.getAttribute('data-overdue') === 'true';
    var defaultHint = hint ? hint.textContent.trim() : '';

    var savedValue = Number(range.value);
    var saveTimer = null;

    var statusLabels = {
        'pending': 'To Do',
        'in-progress': 'In Progress',
        'for-review': 'For Review',
        'returned-for-correction': 'Returned for Correction',
        'completed': 'Completed'
    };
    var statusClasses = {
        'pending': 'todo',
        'in-progress': 'in-progress',
        'for-review': 'for-review',
        'returned-for-correction': 'returned',
        'completed': 'completed'
    };

    function show(value) {
        range.style.setProperty('--pct', value + '%');
        range.setAttribute('aria-valuetext', value + ' percent');
        if (valueLabel) {
            valueLabel.textContent = value + '%';
        }
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
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function applyStatus(status) {
        // An overdue task keeps showing "Overdue" — that overlay is computed
        // from the due date, not from the stored status.
        if (isOverdue || !statusLabels[status]) {
            return;
        }
        if (metaStatus) {
            metaStatus.textContent = statusLabels[status];
        }
        if (badgeText) {
            badgeText.textContent = statusLabels[status];
        }
        if (badge) {
            Object.keys(statusClasses).forEach(function (key) {
                badge.classList.remove(statusClasses[key]);
            });
            badge.classList.add(statusClasses[status]);
        }
    }

    function save() {
        var value = Number(range.value);
        if (value === savedValue) {
            return;
        }

        setHint('Saving…', null);

        var body = new URLSearchParams();
        body.append('id', taskId);
        body.append('progress', String(value));
        body.append('__RequestVerificationToken', getCsrfToken());

        fetch('/Employee/UpdateProgress', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        })
            .then(function (res) {
                return res.json().catch(function () { return null; }).then(function (data) {
                    if (!res.ok) {
                        var message = data && data.message
                            ? data.message
                            : (res.status === 400
                                ? 'The page is out of date. Please refresh the page and try again.'
                                : 'Unable to save your progress. Please try again.');
                        throw new Error(message);
                    }
                    return data;
                });
            })
            .then(function (data) {
                savedValue = data.progress;
                range.value = String(data.progress);
                show(data.progress);
                if (metaProgress) {
                    metaProgress.textContent = data.taskProgress + '%';
                }
                applyStatus(data.taskStatus);
                setHint('Saved — progress is now ' + data.progress + '%.', 'saved');
                setTimeout(function () {
                    if (hint && hint.classList.contains('is-saved')) {
                        setHint(defaultHint, null);
                    }
                }, 3000);
            })
            .catch(function (err) {
                // fetch() throws a TypeError when the server can't be reached.
                var message = err instanceof TypeError
                    ? 'Unable to reach the server. Please check your connection and try again.'
                    : err.message;
                range.value = String(savedValue);
                show(savedValue);
                setHint(message, 'error');
            });
    }

    // Live number while dragging.
    range.addEventListener('input', function () {
        show(Number(range.value));
    });

    // "change" fires when the slider is released (or on each arrow-key
    // press); a short delay groups quick key presses into one save.
    range.addEventListener('change', function () {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(save, 400);
    });
}

/**
 * Delete Task asks for confirmation first, since the delete is permanent and
 * removes the task for everyone (proof files, comments, and history too).
 */
function initDeleteConfirm() {
    var form = document.getElementById('tdDeleteForm');
    if (!form) {
        return;
    }

    form.addEventListener('submit', function (e) {
        var name = form.getAttribute('data-task-name') || 'this task';
        var ok = window.confirm(
            'Permanently delete "' + name + '"?\n\n' +
            'This removes the task for everyone, including its proof files, ' +
            'comments, and history. This cannot be undone.');
        if (!ok) {
            e.preventDefault();
        }
    });
}
