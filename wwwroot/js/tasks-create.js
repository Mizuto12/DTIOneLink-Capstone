// ==========================================================
// DTI Laguna OneLink — Create Task JS
// ==========================================================
(function () {
    "use strict";

    var modal    = document.getElementById("createTaskModal");
    var taskForm = document.getElementById("taskForm");

    // ── Close modal (animate out) ─────────────────────────
    function closeModal() {
        if (!modal) return;
        var box = modal.querySelector(".modal");
        if (box) box.classList.add("closing");
        setTimeout(function () {
            modal.style.display = "none";
        }, 200);
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

    // ── Close on overlay click ────────────────────────────
    if (modal) {
        modal.addEventListener("click", function (e) {
            if (e.target === modal) closeModal();
        });
    }

    // ── Close on Escape ───────────────────────────────────
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeModal();
    });

})();
