// =========================================================
// DTI Laguna Provincial Office — Verify Email, Set / Change /
// Forgot Password interactions. The server repeats every check
// below; these only give faster feedback before the form is sent.
// =========================================================
(function () {

    var MIN_LENGTH = 8; // keep in step with AccountDefaults.MinPasswordLength

    document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll(".toggle-password").forEach(function (btn) {
            var input = btn.closest(".input-wrapper").querySelector("input");
            btn.addEventListener("click", function () {
                var isCurrentlyHidden = input.type === "password";
                input.type = isCurrentlyHidden ? "text" : "password";
                btn.classList.toggle("is-visible", isCurrentlyHidden);
                btn.setAttribute("aria-label", isCurrentlyHidden ? "Hide password" : "Show password");
            });
        });

        // Code box: digits only, so a pasted "123 456" still works.
        var codeInput = document.querySelector(".code-input");
        if (codeInput) {
            codeInput.addEventListener("input", function () {
                codeInput.value = codeInput.value.replace(/\D/g, "").slice(0, 6);
            });
        }

        document.querySelectorAll("form.account-form").forEach(function (form) {
            var current = form.querySelector("[name='CurrentPassword']");
            var newPass = form.querySelector("[name='NewPassword']");
            var confirmPass = form.querySelector("[name='ConfirmPassword']");
            var email = form.querySelector("[name='Email']");
            var code = form.querySelector("[name='Code']");
            var saveButton = form.querySelector(".btn-login");

            [current, newPass, confirmPass, email, code].forEach(function (input) {
                if (!input) return;
                input.addEventListener("input", function () { setError(input, ""); });
            });

            form.addEventListener("submit", function (e) {
                var ok = true;

                if (current && current.value === "") {
                    setError(current, "Current password is required.");
                    ok = false;
                }
                if (newPass) {
                    if (newPass.value.trim() === "") {
                        setError(newPass, "New password is required.");
                        ok = false;
                    } else if (newPass.value.length < MIN_LENGTH) {
                        setError(newPass, "New password must be at least " + MIN_LENGTH + " characters.");
                        ok = false;
                    }
                }
                if (confirmPass) {
                    if (confirmPass.value === "") {
                        setError(confirmPass, "Please confirm your new password.");
                        ok = false;
                    } else if (newPass && confirmPass.value !== newPass.value) {
                        setError(confirmPass, "Passwords do not match.");
                        ok = false;
                    }
                }
                if (email && email.value.trim() === "") {
                    setError(email, "Please enter your email.");
                    ok = false;
                }
                if (code && code.value.length !== 6) {
                    setError(code, "Please enter the 6-digit code.");
                    ok = false;
                }

                if (!ok) {
                    e.preventDefault();
                    if (saveButton) {
                        saveButton.classList.remove("shake");
                        void saveButton.offsetWidth;
                        saveButton.classList.add("shake");
                    }
                    return;
                }

                // Stop double submits (each one would use up a code try).
                if (saveButton) saveButton.disabled = true;
            });
        });

        function setError(input, message) {
            var errorSpan = input.closest(".field-group").querySelector(".field-error");
            input.classList.toggle("input-error", message !== "");
            if (errorSpan) errorSpan.textContent = message;
        }
    });
})();
