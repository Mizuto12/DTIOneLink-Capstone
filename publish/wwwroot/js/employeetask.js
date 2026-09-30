// ==========================================================================
// Laguna Governance – Task Management page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initMobileSidebar();
    initNewTaskButton();
    initFilterButton();
    initTaskCardNavigation();
    initDeleteCompletedTask();
    initCompletedColumnMenu();
});

/**
 * The Completed column's "..." menu. Opens/closes on click, closes on an
 * outside click or Escape, and asks for confirmation (with the number of
 * tasks) before "Delete all completed tasks" is submitted.
 */
function initCompletedColumnMenu() {
    var btn = document.getElementById('completedMenuBtn');
    var panel = document.getElementById('completedMenu');

    if (!btn || !panel) {
        return;
    }

    function setOpen(open) {
        panel.hidden = !open;
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        if (open) {
            var first = panel.querySelector('button');
            if (first) {
                first.focus();
            }
        }
    }

    btn.addEventListener('click', function (event) {
        event.stopPropagation();
        setOpen(panel.hidden);
    });

    document.addEventListener('click', function (event) {
        if (!panel.hidden && !panel.contains(event.target) && event.target !== btn) {
            setOpen(false);
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && !panel.hidden) {
            setOpen(false);
            btn.focus();
        }
    });

    var form = panel.querySelector('.delete-all-form');
    if (form) {
        form.addEventListener('submit', function (event) {
            var count = form.getAttribute('data-count') || 'all';
            var noun = count === '1' ? 'completed task' : 'completed tasks';
            var ok = window.confirm(
                'Permanently delete ' + count + ' ' + noun + '?\n\n' +
                'This removes them for everyone, including their proof files, ' +
                'comments, and history. This cannot be undone.');
            if (!ok) {
                event.preventDefault();
            }
        });
    }
}

/**
 * Toggles the sidebar visibility on small screens when the
 * hamburger button in the top nav is clicked.
 */
function initMobileSidebar() {
    var menuBtn = document.querySelector('.mobile-menu-btn');
    var sidebar = document.querySelector('.sidebar');

    if (!menuBtn || !sidebar) {
        return;
    }

    menuBtn.addEventListener('click', function () {
        var isOpen = sidebar.classList.toggle('is-open');
        sidebar.style.display = isOpen ? 'flex' : 'none';
    });
}

/**
 * Sends the user to the Create task page.
 */
function initNewTaskButton() {
    var newTaskBtn = document.querySelector('[data-action="new-task"]');

    if (!newTaskBtn) {
        return;
    }

    newTaskBtn.addEventListener('click', function () {
        var url = newTaskBtn.getAttribute('data-url');
        if (url) {
            window.location.href = url;
        }
    });
}

/**
 * Placeholder hook for the Filter button. Wire this up to
 * show/hide a filter panel or apply query-string filters as needed.
 */
function initFilterButton() {
    var filterBtn = document.querySelector('[data-action="filter"]');

    if (!filterBtn) {
        return;
    }

    filterBtn.addEventListener('click', function () {
        document.dispatchEvent(new CustomEvent('tasks:filter-toggle'));
    });
}

/**
 * Clicking (or pressing Enter/Space on) a task card opens that
 * task's detail view. Clicks on the pencil "edit" button or the
 * delete button inside a card are excluded so they don't also
 * trigger navigation.
 */
function initTaskCardNavigation() {
    var cards = document.querySelectorAll('.task-card[data-href]');

    cards.forEach(function (card) {
        card.addEventListener('click', function (event) {
            if (event.target.closest('.task-edit-btn, .task-delete-form')) {
                return;
            }
            var href = card.getAttribute('data-href');
            if (href) {
                window.location.href = href;
            }
        });

        card.addEventListener('keydown', function (event) {
            if (event.key !== 'Enter' && event.key !== ' ') {
                return;
            }
            if (event.target.closest('.task-edit-btn, .task-delete-form')) {
                return;
            }
            event.preventDefault();
            var href = card.getAttribute('data-href');
            if (href) {
                window.location.href = href;
            }
        });
    });
}

/**
 * Delete button on a Completed card. Asks for confirmation (the delete is
 * permanent and removes the task for everyone), then submits the card's own
 * form to /Employee/Delete, which re-checks everything on the server.
 */
function initDeleteCompletedTask() {
    var forms = document.querySelectorAll('.task-delete-form');

    forms.forEach(function (form) {
        form.addEventListener('submit', function (event) {
            var name = form.getAttribute('data-task-name') || 'this task';
            var ok = window.confirm(
                'Permanently delete "' + name + '"?\n\n' +
                'This removes the task for everyone, including its proof files, ' +
                'comments, and history. This cannot be undone.');
            if (!ok) {
                event.preventDefault();
            }
        });
    });
}
