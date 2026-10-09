/* ==========================================================================
   Automatic capitalization for formal text boxes.
   A box opts in with data-capitalize="title" (titles, names, positions) or
   data-capitalize="sentence" (descriptions). It is formatted when the user
   leaves the box and again just before its form is saved — never while
   typing, so the cursor never jumps. Read-only boxes are left alone.
   ========================================================================== */
(function () {
    "use strict";

    // The office's own acronyms: upper-cased even when typed in lowercase.
    // Words that are also ordinary English ("it", "sap") are not listed; they
    // stay upper-case only when typed that way.
    var KNOWN = ["DTI", "OPD", "BDD", "CPD", "FAU", "PGS", "ICT", "MSME", "LGU", "ITSM", "HR", "API", "SQL"];

    // Emails, links, codes with digits (TASK-0005, Q3, 2024) and file names
    // (report.xlsx) keep exactly what was typed.
    function isSpecial(core, token) {
        return /@|:\/\/|^www\./i.test(token) || /\d/.test(core) || core.indexOf(".") !== -1;
    }

    // Split a token into leading punctuation, the word, trailing punctuation.
    function parts(token) {
        var m = /^([^\p{L}\p{N}]*)(.*?)([^\p{L}\p{N}]*)$/u.exec(token);
        return { lead: m[1], core: m[2], trail: m[3] };
    }

    function capWord(word, shouting) {
        if (!word) return word;
        var upper = word.toUpperCase();
        if (KNOWN.indexOf(upper) !== -1) return upper;
        // An all-capitals word in an otherwise normal box is an acronym.
        if (!shouting && word.length >= 2 && word === upper && word !== word.toLowerCase()) return word;
        return word.charAt(0).toUpperCase() + word.slice(1).toLowerCase();
    }

    // "conduct monitoring for DTI using ITSM" -> "Conduct Monitoring For DTI Using ITSM"
    function toTitle(text) {
        var letters = text.replace(/[^\p{L}]/gu, "");
        var words = text.trim().split(/\s+/).filter(function (w) { return /\p{L}/u.test(w); });
        // A whole box in capitals ("MARK DE JESUS") is caps lock, not acronyms.
        var shouting = words.length >= 2 && letters.length >= 4 && letters === letters.toUpperCase() && letters !== letters.toLowerCase();

        return text.split(/(\s+)/).map(function (token) {
            if (!token || /^\s+$/.test(token)) return token;
            var p = parts(token);
            if (!p.core || isSpecial(p.core, token)) return token;
            // Each part of "follow-up" or "and/or" is its own word.
            var core = p.core.split(/([-\/])/).map(function (piece) {
                return piece === "-" || piece === "/" ? piece : capWord(piece, shouting);
            }).join("");
            return p.lead + core + p.trail;
        }).join("");
    }

    // Capitalizes the first letter of each sentence; everything else as typed.
    function toSentence(text) {
        var start = true;
        return text.split(/(\s+)/).map(function (token) {
            if (!token) return token;
            if (/^\s+$/.test(token)) {
                if (token.indexOf("\n") !== -1) start = true;
                return token;
            }
            var out = token;
            var p = parts(token);
            if (start && p.core && !isSpecial(p.core, token)) {
                out = p.lead + p.core.charAt(0).toUpperCase() + p.core.slice(1) + p.trail;
            }
            if (/\p{L}|\p{N}/u.test(token)) start = /[.!?]["')\]]*$/.test(token);
            return out;
        }).join("");
    }

    function format(el) {
        if (el.readOnly || el.disabled || !el.value) return;
        var mode = el.getAttribute("data-capitalize");
        var next = mode === "sentence" ? toSentence(el.value) : toTitle(el.value);
        if (next !== el.value) el.value = next;
    }

    var SELECTOR = "input[data-capitalize], textarea[data-capitalize]";

    document.addEventListener("focusout", function (e) {
        if (e.target.matches && e.target.matches(SELECTOR)) format(e.target);
    });

    // Capture phase: runs before the page's own submit handlers read the values.
    // form.elements also covers boxes linked with form="…" from elsewhere.
    document.addEventListener("submit", function (e) {
        Array.prototype.forEach.call(e.target.elements, function (el) {
            if (el.matches(SELECTOR)) format(el);
        });
    }, true);

    window.TextFormat = { title: toTitle, sentence: toSentence };
})();
