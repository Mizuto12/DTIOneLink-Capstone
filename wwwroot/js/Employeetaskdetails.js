// ==========================================================================
// Laguna Governance – Task Details page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initCopyTaskCode();
    initBackButton();
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