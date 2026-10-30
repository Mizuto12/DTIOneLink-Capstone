// =========================================================
// DTI Laguna Provincial Office — Login Page interactions
// =========================================================
(function () {

    document.addEventListener("DOMContentLoaded", function () {
        var form = document.getElementById("loginForm");
        var emailInput = document.querySelector("[name='Email']");
        var passwordInput = document.querySelector("[name='Password']");
        var loginButton = document.querySelector(".btn-login");
        var togglePasswordBtn = document.getElementById("togglePassword");

        if (form) {
            form.addEventListener("submit", function (e) {
                var hasError = false;

                hasError = !validateRequired(emailInput) || hasError;
                hasError = !validateRequired(passwordInput) || hasError;

                if (hasError) {
                    e.preventDefault();
                    shakeButton();
                    return;
                }

                // Stop a slow connection from inviting a second click, which
                // would resubmit the password and burn another rate-limit hit.
                if (loginButton) loginButton.disabled = true;
            });
        }

        [emailInput, passwordInput].forEach(function (input) {
            if (!input) return;
            input.addEventListener("input", function () {
                input.classList.remove("input-error");
                var errorSpan = input.closest(".field-group").querySelector(".field-error");
                if (errorSpan) errorSpan.textContent = "";
            });
        });

        if (togglePasswordBtn && passwordInput) {
            togglePasswordBtn.addEventListener("click", function () {
                var isCurrentlyHidden = passwordInput.type === "password";
                passwordInput.type = isCurrentlyHidden ? "text" : "password";
                togglePasswordBtn.classList.toggle("is-visible", isCurrentlyHidden);
                togglePasswordBtn.setAttribute("aria-label", isCurrentlyHidden ? "Hide password" : "Show password");
            });
        }

        function shakeButton() {
            if (!loginButton) return;
            loginButton.classList.remove("shake");
            void loginButton.offsetWidth;
            loginButton.classList.add("shake");
        }

        function validateRequired(input) {
            if (!input) return true;
            var errorSpan = input.closest(".field-group").querySelector(".field-error");
            var value = input.value.trim();

            if (value === "") {
                input.classList.add("input-error");
                if (errorSpan) {
                    errorSpan.textContent = (input.getAttribute("name") || "This field") + " is required.";
                }
                return false;
            }

            input.classList.remove("input-error");
            if (errorSpan) errorSpan.textContent = "";
            return true;
        }
    });
})();
