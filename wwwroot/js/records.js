// ==========================================================
// DTI Laguna OneLink — Records Management JS
// ==========================================================
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {

        // ── Submit micro-interaction: saving -> success -> reset ──
        var form = document.getElementById("recordForm");
        if (!form) {
            return;
        }

        var button = form.querySelector(".rec-submit");

        form.addEventListener("submit", function (e) {
            e.preventDefault();

            if (!button || button.disabled) {
                return;
            }

            var originalContent = button.innerHTML;

            button.innerHTML =
                '<span class="material-symbols-outlined rec-spin">sync</span> Recording...';
            button.disabled = true;

            setTimeout(function () {
                button.innerHTML =
                    '<span class="material-symbols-outlined">check_circle</span> Success!';
                button.classList.add("is-success");

                setTimeout(function () {
                    button.innerHTML = originalContent;
                    button.classList.remove("is-success");
                    button.disabled = false;
                    form.reset();
                }, 2000);
            }, 1500);
        });
    });
})();
