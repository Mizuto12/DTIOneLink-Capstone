// ==========================================================================
// Laguna Governance – Task Details page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initCopyTaskCode();
    initBackButton();
    initProgressSlider();
    initDeleteConfirm();
    initActivityFade();
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

    // Progress ring (r=58; circumference = 2*pi*58).
    var RING_CIRCUMFERENCE = 364.425;
    var ringArc = document.getElementById('tdProgressRingArc');
    var ringValue = document.getElementById('tdProgressRingValue');
    var unlockGate = document.getElementById('tdUnlockGate');
    var unlockGateText = document.getElementById('tdUnlockGateText');

    var savedValue = Number(range.value);
    var saveTimer = null;

    var submitGate = document.getElementById('tdSubmitGate');
    var submitBtn = document.getElementById('tdSubmitProofBtn');
    var submitHint = document.getElementById('tdSubmitHint');
    // The range always carries this when the slider itself renders (same
    // condition the unlock-gate card renders under); the Submit Proof
    // button/gate only renders for the legacy single-assignee case, so
    // prefer the range's own copy over requiring both to agree.
    var minProgressAttr = range.getAttribute('data-min-progress')
        || (submitGate ? submitGate.getAttribute('data-min-progress') : null);
    var minProgress = minProgressAttr !== null ? Number(minProgressAttr) : null;

    // Mirrors TaskWorkflow.CanSubmitProof: locked unless progress has reached
    // the threshold (status is always pending/in-progress here — the gate
    // markup only renders in that branch; Returned-for-correction resubmits
    // through a separate, always-enabled link).
    function updateSubmitGate(progress) {
        // The unlock-requirement headline lives in the progress hero, not
        // just as a footnote under the button — update both together so
        // they can never disagree about whether proof can be submitted yet.
        if (unlockGate) {
            var stillLocked = minProgress !== null && progress < minProgress;
            unlockGate.hidden = !stillLocked;
            if (stillLocked && unlockGateText) {
                var pointsToGo = minProgress - progress;
                unlockGateText.textContent = pointsToGo + ' point' + (pointsToGo === 1 ? '' : 's') + ' to go to unlock Submit Proof';
            }
        }

        if (!submitGate || !submitBtn || minProgress === null) {
            return;
        }
        var unlocked = progress >= minProgress;
        submitBtn.classList.toggle('is-locked', !unlocked);
        submitBtn.setAttribute('aria-disabled', unlocked ? 'false' : 'true');
        submitBtn.setAttribute('tabindex', unlocked ? '0' : '-1');
        submitBtn.title = unlocked ? '' : 'Reach at least ' + minProgress + '% progress to submit proof.';
        if (submitHint) {
            submitHint.hidden = unlocked;
        }
    }

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
        if (ringArc) {
            ringArc.style.strokeDashoffset = String(RING_CIRCUMFERENCE * (1 - value / 100));
        }
        if (ringValue) {
            ringValue.textContent = value + '%';
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
                updateSubmitGate(data.progress);
                setHint('Saved — progress is now ' + data.progress + '%.', 'saved');
                setTimeout(function () {
                    if (hint && hint.classList.contains('is-saved')) {
                        setHint(defaultHint, null);
                    }
                }, 3000);
            })
            .catch(function (err) {
                // fetch() throws a TypeError when the server can't be reached.
                // Rather than discard the change, queue it for offline-sync.js
                // to replay automatically once the connection returns — the
                // slider keeps the value the user set instead of reverting.
                if (err instanceof TypeError && window.DTIOffline) {
                    window.DTIOffline.queueProgress({ taskId: Number(taskId), progress: value });
                    savedValue = value;
                    updateSubmitGate(value);
                    setHint('Saved offline — will sync automatically when you\'re back online.', 'saved');
                    return;
                }

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
 * Activity History's scroll-aware fade: a soft mask over the list's last
 * ~32px, shown only while there's more to scroll to, and removed once
 * scrolled to the real end — so it never implies there's more below when
 * there isn't, in either direction.
 */
function initActivityFade() {
    var scrollEl = document.getElementById('tdTimeline');
    var wrapper = document.getElementById('tdTimelineScroll');
    if (!scrollEl || !wrapper) {
        return;
    }

    function update() {
        var hasMore = scrollEl.scrollHeight - scrollEl.scrollTop - scrollEl.clientHeight > 1;
        wrapper.classList.toggle('has-more', hasMore);
    }

    scrollEl.addEventListener('scroll', update);
    window.addEventListener('resize', update);
    update();
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
        if (form.dataset.confirmed === 'true') {
            return;
        }
        e.preventDefault();

        var name = form.getAttribute('data-task-name') || 'this task';
        window.showNotice({
            tone: 'danger',
            icon: 'delete',
            title: 'Permanently delete "' + name + '"?',
            message: 'This removes the task for everyone, including its proof files, comments, and history. This cannot be undone.',
            okText: 'Delete',
            cancelText: 'Cancel'
        }).then(function (ok) {
            if (ok) {
                form.dataset.confirmed = 'true';
                form.requestSubmit ? form.requestSubmit() : form.submit();
            }
        });
    });
}
