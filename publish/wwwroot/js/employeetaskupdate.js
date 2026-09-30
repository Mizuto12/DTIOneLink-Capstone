document.addEventListener('DOMContentLoaded', function () {
    initProgressSlider();
    initStartWorkCheckbox();
    initBackButton();
});

function initProgressSlider() {
    var range = document.getElementById('tuProgressRange');
    if (!range) return;

    var valueLabel = document.getElementById('tuProgressValue');
    var fill = document.getElementById('tuProgressFill');
    var requestedStatus = document.getElementById('tuRequestedStatus');
    var startWork = document.getElementById('tuStartWork');

    range.addEventListener('input', function () {
        var value = range.value;
        valueLabel.textContent = value + '%';
        fill.style.width = value + '%';

        // Moving progress above 0 implies work has started — this is a
        // UX nicety only; the server enforces the same rule regardless
        // of what gets posted here.
        if (requestedStatus && Number(value) > 0) {
            requestedStatus.value = 'in-progress';
            if (startWork) {
                startWork.checked = true;
            }
        }
    });
}

function initStartWorkCheckbox() {
    var checkbox = document.getElementById('tuStartWork');
    if (!checkbox) return;

    var requestedStatus = document.getElementById('tuRequestedStatus');

    checkbox.addEventListener('change', function () {
        requestedStatus.value = checkbox.checked ? 'in-progress' : 'pending';
    });
}

function initBackButton() {
    var btn = document.getElementById('tuBackButton');
    if (!btn) return;

    btn.addEventListener('click', function () {
        window.history.back();
    });
}