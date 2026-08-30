// ==========================================================================
// Laguna Governance – Task Management page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initMobileSidebar();
    initNewTaskButton();
    initFilterButton();
    initTaskCardNavigation();
});

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
 * task's detail view. Clicks on the pencil "edit" button inside a
 * card are excluded so they don't also trigger navigation.
 */
function initTaskCardNavigation() {
    var cards = document.querySelectorAll('.task-card[data-href]');

    cards.forEach(function (card) {
        card.addEventListener('click', function (event) {
            if (event.target.closest('.task-edit-btn')) {
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
            if (event.target.closest('.task-edit-btn')) {
                return;
            }
            event.preventDefault();
            var href = card.getAttribute('data-href');
            if (href) {
                window.location.href = href;
            }
        });
    });
}// ==========================================================================
// Laguna Governance – Task Management page behavior
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initMobileSidebar();
    initNewTaskButton();
    initFilterButton();
    initTaskCardNavigation();
});

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
 * task's detail view. Clicks on the pencil "edit" button inside a
 * card are excluded so they don't also trigger navigation.
 */
function initTaskCardNavigation() {
    var cards = document.querySelectorAll('.task-card[data-href]');

    cards.forEach(function (card) {
        card.addEventListener('click', function (event) {
            if (event.target.closest('.task-edit-btn')) {
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
            if (event.target.closest('.task-edit-btn')) {
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