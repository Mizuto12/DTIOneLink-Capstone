// ==========================================================================
// Shared inline help hint. Markup pattern (anywhere on the page):
//
//   <button type="button" class="help-hint" aria-label="What does X mean?"
//           aria-expanded="false" data-help-text="Plain-language explanation.">?</button>
//
// Click toggles a small popover right under the button showing
// data-help-text; click elsewhere, or Escape, closes it. Delegated at the
// document level, so any number of .help-hint buttons on a page work with
// no per-element setup — just drop the markup in.
// ==========================================================================
(function () {
    var openPopover = null;
    var openButton = null;

    function close() {
        if (openPopover) {
            openPopover.remove();
            openPopover = null;
        }
        if (openButton) {
            openButton.setAttribute('aria-expanded', 'false');
            openButton = null;
        }
    }

    function open(button) {
        close();
        var popover = document.createElement('span');
        popover.className = 'help-hint-popover';
        popover.setAttribute('role', 'tooltip');
        popover.textContent = button.getAttribute('data-help-text') || '';

        // Position relative to the button's own offset parent, directly
        // under it, nudged left if it would overflow the viewport's right edge.
        button.insertAdjacentElement('afterend', popover);
        popover.style.position = 'absolute';
        var rect = button.getBoundingClientRect();
        var popRect = popover.getBoundingClientRect();
        var overflowRight = rect.left + popRect.width - window.innerWidth + 16;
        popover.style.left = (overflowRight > 0 ? -overflowRight : 0) + 'px';
        popover.style.top = '100%';

        // The button needs a positioning context for the popover's
        // absolute placement to land relative to it, not the page.
        var parent = button.parentElement;
        if (parent && getComputedStyle(parent).position === 'static') {
            parent.style.position = 'relative';
        }

        button.setAttribute('aria-expanded', 'true');
        openPopover = popover;
        openButton = button;
    }

    document.addEventListener('click', function (event) {
        var button = event.target.closest('.help-hint');
        if (button) {
            event.preventDefault();
            event.stopPropagation();
            if (openButton === button) {
                close();
            } else {
                open(button);
            }
            return;
        }
        if (openPopover && !openPopover.contains(event.target)) {
            close();
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && openPopover) {
            var toRefocus = openButton;
            close();
            if (toRefocus) toRefocus.focus();
        }
    });
})();
