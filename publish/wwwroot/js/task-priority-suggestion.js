// ==========================================================================
// Task Edit/Create — live priority suggestion
// Fetches a rule-based priority suggestion from TasksController.SuggestPriority
// whenever the due date changes. Never auto-applies — Admin/Supervisor must
// click "Use this" to accept it, or pick a radio button directly to override.
// ==========================================================================
document.addEventListener('DOMContentLoaded', function () {
    initPrioritySuggestion();
});

function initPrioritySuggestion() {
    var dueDateInput = document.getElementById('DueDate') || document.querySelector('[name="DueDate"]');
    var suggestionText = document.getElementById('prioritySuggestionText');
    var applyBtn = document.getElementById('applyPrioritySuggestion');

    if (!dueDateInput || !suggestionText || !applyBtn) {
        return;
    }

    function radioFor(priority) {
        return document.getElementById('priority' + priority.charAt(0).toUpperCase() + priority.slice(1));
    }

    function refreshSuggestion() {
        if (!dueDateInput.value) return;

        fetch('/Tasks/SuggestPriority?dueDate=' + encodeURIComponent(dueDateInput.value))
            .then(function (res) {
                if (!res.ok) throw new Error('Suggestion request failed');
                return res.json();
            })
            .then(function (data) {
                suggestionText.textContent = data.reason;
                applyBtn.setAttribute('data-priority', data.priority);
            })
            .catch(function () {
                suggestionText.textContent = 'Could not load a suggestion right now.';
            });
    }

    dueDateInput.addEventListener('change', refreshSuggestion);

    applyBtn.addEventListener('click', function () {
        var priority = applyBtn.getAttribute('data-priority');
        var radio = radioFor(priority);
        if (radio) {
            radio.checked = true;
        }
    });
}