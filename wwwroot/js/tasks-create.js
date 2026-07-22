// ==========================================================
// DTI Laguna OneLink — Create Task modal (in-place overlay)
// ==========================================================
(function () {
    "use strict";

    var modal    = document.getElementById("createTaskModal");
    var taskForm = document.getElementById("taskForm");
    var openBtn  = document.getElementById("openCreateTask");

    if (!modal) return;

    var box = modal.querySelector(".modal");

    // ── Open modal ────────────────────────────────────────
    function openModal() {
        if (box) box.classList.remove("closing");
        modal.classList.remove("is-hidden");
    }

    // ── Close modal (animate out) ─────────────────────────
    function closeModal() {
        if (box) box.classList.add("closing");
        setTimeout(function () {
            modal.classList.add("is-hidden");
            if (box) box.classList.remove("closing");
        }, 200);
    }

    // ── Triggers ──────────────────────────────────────────
    if (openBtn) {
        openBtn.addEventListener("click", openModal);
    }

    modal.querySelectorAll("[data-close-modal]").forEach(function (el) {
        el.addEventListener("click", closeModal);
    });

    // Close on overlay click (but not clicks inside the box)
    modal.addEventListener("click", function (e) {
        if (e.target === modal) closeModal();
    });

    // Close on Escape
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !modal.classList.contains("is-hidden")) closeModal();
    });

    // ── Submit handler — loading state ────────────────────
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

})();
