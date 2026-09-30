// Filters the Responsible Admin <select> to only the options whose
// data-department matches the currently-selected Target Department. Every
// active Admin is already rendered into the DOM by the server (see
// Create.cshtml) — this never issues a network request, so the list can't
// come back empty because a fetch failed. Enforcement that the final
// selection actually belongs to the task's department still happens
// server-side (ValidateResponsibleAdminAsync), regardless of this filter.
(function () {
    'use strict';

    function init() {
        var deptSelect = document.getElementById('targetDepartment');
        var adminSelect = document.querySelector('[data-responsible-admin-select]');
        if (!deptSelect || !adminSelect) return;

        var allOptions = Array.prototype.slice.call(adminSelect.options);

        function apply() {
            var department = deptSelect.value;

            allOptions.forEach(function (opt) {
                if (opt.value === '') {
                    opt.hidden = false; // always keep the placeholder visible
                    return;
                }
                // Do not offer an Admin until a department is selected; this
                // prevents a cross-department selection before filtering.
                var matches = Boolean(department) &&
                    opt.getAttribute('data-department') === department;
                opt.hidden = !matches;
            });

            // If the currently-selected admin no longer matches, reset to
            // the placeholder rather than silently keeping an invalid pick.
            var selected = adminSelect.options[adminSelect.selectedIndex];
            if (selected && selected.hidden) {
                adminSelect.value = '';
            }
        }

        deptSelect.addEventListener('change', apply);
        apply();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
