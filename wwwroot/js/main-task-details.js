// ==========================================================================
// Main Task Details — confirm before reassigning the Responsible Admin
// (OPD only), the same way Stop Repeating a few lines above it already
// does. Handing a directive to a different Admin is at least as
// consequential as stopping a repeat, so it gets the same care: name the
// task and the Admin before committing.
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    var form = document.getElementById('mtdReassignForm');
    var select = document.getElementById('mtdReassignSelect');
    if (!form || !select) {
        return;
    }

    var taskName = form.getAttribute('data-task-name') || 'this task';

    form.addEventListener('submit', function (event) {
        var chosenOption = select.options[select.selectedIndex];
        var adminName = chosenOption ? chosenOption.textContent.trim() : 'this Admin';

        var message = 'Reassign "' + taskName + '" to ' + adminName + '?\n\n' +
            'They will be notified and will become responsible for this task.';

        if (!window.confirm(message)) {
            event.preventDefault();
        }
    });
});
