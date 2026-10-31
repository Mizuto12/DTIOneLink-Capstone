// Progressive-enhancement combo box for the Assignees field on the
// Admin Task Create/Edit forms. The real <input type="checkbox" name="AssigneeIds">
// elements stay in the DOM and drive form submission exactly as before —
// this script only manages the open/closed UI and the trigger label.
(function () {
    'use strict';

    function initAssigneeSelect(root) {
        var trigger = root.querySelector('[data-assignee-trigger]');
        var panel = root.querySelector('[data-assignee-panel]');
        var label = root.querySelector('[data-assignee-label]');
        var search = root.querySelector('[data-assignee-search]');
        var options = Array.prototype.slice.call(root.querySelectorAll('[data-assignee-option]'));
        var emptyMsg = root.querySelector('[data-assignee-empty]');
        var checkboxes = options.map(function (opt) {
            return opt.querySelector('input[type="checkbox"]');
        });

        if (!trigger || !panel || !label) return;

        function isOpen() {
            return !panel.hasAttribute('hidden');
        }

        // The panel is CSS-positioned absolute by default, but its nearest
        // "card" ancestor (.et-workspace on Edit, the equivalent on Create)
        // has overflow:hidden for its rounded corners — which also clips
        // anything inside that tries to extend past the card, cutting the
        // dropdown off instead of letting it float over the page. Switching
        // to fixed positioning, placed via the trigger's own on-screen
        // rect, escapes that clipping regardless of which page this is on
        // or how it's scrolled. Inline styles always win over the CSS
        // absolute-positioning rule, so no CSS changes are needed.
        function positionPanel() {
            var rect = trigger.getBoundingClientRect();
            panel.style.position = 'fixed';
            panel.style.top = (rect.bottom + 6) + 'px';
            panel.style.left = rect.left + 'px';
            panel.style.width = rect.width + 'px';
        }

        function openPanel() {
            if (isOpen()) return;
            positionPanel();
            panel.removeAttribute('hidden');
            trigger.setAttribute('aria-expanded', 'true');
            root.classList.add('is-open');
            if (search) {
                search.value = '';
                filterOptions('');
                window.setTimeout(function () { search.focus(); }, 0);
            }
            document.addEventListener('click', onDocumentClick, true);
            document.addEventListener('keydown', onDocumentKeydown, true);
            // capture:true so this still fires on a scroll inside a nested
            // scrollable area (e.g. the tab card itself), not just the window.
            window.addEventListener('scroll', positionPanel, true);
            window.addEventListener('resize', positionPanel);
        }

        function closePanel(refocusTrigger) {
            if (!isOpen()) return;
            panel.setAttribute('hidden', '');
            trigger.setAttribute('aria-expanded', 'false');
            root.classList.remove('is-open');
            document.removeEventListener('click', onDocumentClick, true);
            document.removeEventListener('keydown', onDocumentKeydown, true);
            window.removeEventListener('scroll', positionPanel, true);
            window.removeEventListener('resize', positionPanel);
            if (refocusTrigger) trigger.focus();
        }

        function togglePanel() {
            if (isOpen()) {
                closePanel(false);
            } else {
                openPanel();
            }
        }

        function onDocumentClick(evt) {
            if (!root.contains(evt.target)) {
                closePanel(false);
            }
        }

        function onDocumentKeydown(evt) {
            if (evt.key === 'Escape' || evt.key === 'Esc') {
                evt.stopPropagation();
                closePanel(true);
            }
        }

        function updateLabel() {
            var names = checkboxes
                .filter(function (cb) { return cb.checked; })
                .map(function (cb) {
                    return cb.closest('[data-assignee-option]').getAttribute('data-display-name') || '';
                });

            if (names.length === 0) {
                label.textContent = 'Select Assignees';
                label.classList.add('is-placeholder');
            } else if (names.length <= 2) {
                label.textContent = names.join(', ');
                label.classList.remove('is-placeholder');
            } else {
                label.textContent = names.length + ' employees selected';
                label.classList.remove('is-placeholder');
            }
        }

        function filterOptions(query) {
            var q = query.trim().toLowerCase();
            var visibleCount = 0;
            options.forEach(function (opt) {
                var name = opt.getAttribute('data-name') || '';
                var match = name.indexOf(q) !== -1;
                opt.hidden = !match;
                if (match) visibleCount++;
            });
            if (emptyMsg) emptyMsg.hidden = visibleCount !== 0;
        }

        // Native <button> activation already fires 'click' for both
        // Enter and Space, so a single click handler covers requirement 9's
        // "button/Enter/Space opens it" without double-handling keys.
        trigger.addEventListener('click', togglePanel);

        trigger.addEventListener('keydown', function (evt) {
            if (evt.key === 'Escape') {
                closePanel(true);
            }
        });

        if (search) {
            search.addEventListener('input', function () {
                filterOptions(search.value);
            });
        }

        checkboxes.forEach(function (cb) {
            cb.addEventListener('change', updateLabel);
        });

        // Initial render — picks up pre-checked boxes on the Edit form.
        updateLabel();
    }

    function init() {
        var roots = document.querySelectorAll('[data-assignee-select]');
        Array.prototype.forEach.call(roots, initAssigneeSelect);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();