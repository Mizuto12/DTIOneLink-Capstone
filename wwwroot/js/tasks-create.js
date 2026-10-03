// ==========================================================
// DTI Laguna OneLink — Create Task JS
// ==========================================================
(function () {
    "use strict";

    var modal    = document.getElementById("createTaskModal");
    var taskForm = document.getElementById("taskForm");

    // A native modal dialog sits above everything (top bar and sidebar
    // included) and dims the real Task Management list behind it.
    if (modal && typeof modal.showModal === "function") {
        modal.showModal();
    } else if (modal) {
        modal.setAttribute("open", ""); // very old browsers: shown, not modal
    }

    // ── Close: animate out, then back to Task Management ──
    function closeModal() {
        if (!modal) return;
        var box = modal.querySelector(".modal");
        if (box) box.classList.add("closing");
        setTimeout(function () {
            window.location.href = modal.dataset.closeUrl || "/Tasks";
        }, 150);
    }

    // ── Submit handler — intercept for loading state ──────
    if (taskForm) {
        taskForm.addEventListener("submit", function () {
            var btn = document.getElementById("createBtn");
            if (!btn) return;

            var spinIcon = document.createElement("span");
            spinIcon.className = "material-symbols-outlined spin";
            spinIcon.textContent = "refresh";
            btn.innerHTML = "";
            btn.appendChild(spinIcon);
            btn.appendChild(document.createTextNode(" Creating..."));
            btn.disabled = true;
        });
    }

    // ── Close on a click outside the form ─────────────────
    // The dialog fills the screen, so a click on it (not on .modal) is a
    // click on the dimmed area around the form. Only while nothing has been
    // typed, so a stray click never throws away a half-filled form.
    // Coming back with errors means the form already holds their entries.
    var touched = !!document.querySelector("#createTaskModal .validation-summary");
    if (taskForm) {
        taskForm.addEventListener("input", function () { touched = true; });
        taskForm.addEventListener("change", function () { touched = true; });
    }
    if (modal) {
        modal.addEventListener("click", function (e) {
            if (e.target === modal && !touched) closeModal();
        });

        // ── Close on Escape (the browser fires "cancel") ──
        modal.addEventListener("cancel", function (e) {
            e.preventDefault();
            closeModal();
        });
    }

})();
