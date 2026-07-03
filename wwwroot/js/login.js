// =========================================================
// DTI Laguna Provincial Office — Login Page interactions
// =========================================================
(function () {

    document.addEventListener("DOMContentLoaded", function () {
        var form = document.getElementById("loginForm");
        var usernameInput = document.querySelector("[name='Username']");
        var passwordInput = document.querySelector("[name='Password']");
        var loginButton = document.querySelector(".btn-login");
        var forgotLink = document.getElementById("forgotPasswordLink");
        var togglePasswordBtn = document.getElementById("togglePassword");

        if (form) {
            form.addEventListener("submit", function (e) {
                var hasError = false;

                hasError = !validateRequired(usernameInput) || hasError;
                hasError = !validateRequired(passwordInput) || hasError;

                if (hasError) {
                    e.preventDefault();
                    shakeButton();
                }
            });
        }

        [usernameInput, passwordInput].forEach(function (input) {
            if (!input) return;
            input.addEventListener("input", function () {
                input.classList.remove("input-error");
                var errorSpan = input.closest(".field-group").querySelector(".field-error");
                if (errorSpan) errorSpan.textContent = "";
            });
        });

        if (forgotLink) {
            forgotLink.addEventListener("click", function (e) {
                e.preventDefault();
                window.alert("Please contact the DTI Laguna Provincial Office system administrator to reset your password.");
            });
        }

        if (togglePasswordBtn && passwordInput) {
            togglePasswordBtn.addEventListener("click", function () {
                var isCurrentlyHidden = passwordInput.type === "password";
                passwordInput.type = isCurrentlyHidden ? "text" : "password";
                togglePasswordBtn.classList.toggle("is-visible", isCurrentlyHidden);
                togglePasswordBtn.setAttribute("aria-label", isCurrentlyHidden ? "Hide password" : "Show password");
            });
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
