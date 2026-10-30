// Gmail/Outlook-style "To" field: type to search, pick from suggestions,
// selections become removable chips. The chips carry plain hidden
// <input name="..."> elements with the same field name the server already
// binds to a List<int> — this only replaces how those values get picked,
// nothing about how the form posts or how the controller reads it.
(function () {
    'use strict';

    function initChipInput(root) {
        var fieldName = root.getAttribute('data-chip-name');
        var optionsEl = document.getElementById(root.getAttribute('data-chip-options'));
        var chipsWrap = root.querySelector('[data-chip-chips]');
        var textInput = root.querySelector('[data-chip-text]');
        var suggestionsEl = root.querySelector('[data-chip-suggestions]');
        if (!fieldName || !optionsEl || !chipsWrap || !textInput || !suggestionsEl) return;

        var allOptions = [];
        try { allOptions = JSON.parse(optionsEl.textContent || '[]'); } catch (e) { allOptions = []; }

        var selectedIds = (root.getAttribute('data-chip-selected') || '')
            .split(',')
            .map(function (s) { return parseInt(s, 10); })
            .filter(function (n) { return !isNaN(n) && byId(n); });

        var highlightIndex = -1;
        var visibleMatches = [];

        function byId(id) {
            for (var i = 0; i < allOptions.length; i++) {
                if (allOptions[i].id === id) return allOptions[i];
            }
            return null;
        }

        function renderChips() {
            chipsWrap.innerHTML = '';
            selectedIds.forEach(function (id) {
                var opt = byId(id);
                if (!opt) return;

                var chip = document.createElement('span');
                chip.className = 'chip';

                var label = document.createElement('span');
                label.className = 'chip-label';
                label.textContent = opt.name;
                chip.appendChild(label);

                var remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'chip-remove';
                remove.setAttribute('aria-label', 'Remove ' + opt.name);
                remove.textContent = '×';
                remove.addEventListener('click', function () { removeChip(id); });
                chip.appendChild(remove);

                var hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = fieldName;
                hidden.value = String(id);
                chip.appendChild(hidden);

                chipsWrap.appendChild(chip);
            });
        }

        function addChip(id) {
            if (selectedIds.indexOf(id) !== -1 || !byId(id)) return;
            selectedIds.push(id);
            renderChips();
            textInput.value = '';
            closeSuggestions();
            textInput.focus();
        }

        function removeChip(id) {
            selectedIds = selectedIds.filter(function (existing) { return existing !== id; });
            renderChips();
            textInput.focus();
        }

        function openSuggestions() {
            suggestionsEl.hidden = false;
            textInput.setAttribute('aria-expanded', 'true');
        }

        function closeSuggestions() {
            suggestionsEl.hidden = true;
            textInput.setAttribute('aria-expanded', 'false');
            highlightIndex = -1;
        }

        function updateHighlight() {
            var items = suggestionsEl.querySelectorAll('[data-index]');
            Array.prototype.forEach.call(items, function (item) {
                var isHighlighted = parseInt(item.getAttribute('data-index'), 10) === highlightIndex;
                item.classList.toggle('is-highlighted', isHighlighted);
                if (isHighlighted && item.scrollIntoView) item.scrollIntoView({ block: 'nearest' });
            });
        }

        function renderSuggestions(query) {
            var q = query.trim().toLowerCase();
            visibleMatches = allOptions.filter(function (opt) {
                if (selectedIds.indexOf(opt.id) !== -1) return false;
                if (q === '') return true;
                return opt.name.toLowerCase().indexOf(q) !== -1 ||
                    (opt.email || '').toLowerCase().indexOf(q) !== -1;
            });

            suggestionsEl.innerHTML = '';
            highlightIndex = visibleMatches.length > 0 ? 0 : -1;

            if (visibleMatches.length === 0) {
                var empty = document.createElement('p');
                empty.className = 'chip-suggestions-empty';
                empty.textContent = q === '' ? 'No more people to add' : 'No matching person found';
                suggestionsEl.appendChild(empty);
                openSuggestions();
                return;
            }

            visibleMatches.forEach(function (opt, index) {
                var item = document.createElement('div');
                item.className = 'chip-suggestion' + (index === highlightIndex ? ' is-highlighted' : '');
                item.setAttribute('role', 'option');
                item.setAttribute('data-index', String(index));

                var name = document.createElement('span');
                name.className = 'chip-suggestion-name';
                name.textContent = opt.name;
                item.appendChild(name);

                var meta = document.createElement('span');
                meta.className = 'chip-suggestion-meta';
                meta.textContent = (opt.email || '') + (opt.department ? ' · ' + opt.department : '');
                item.appendChild(meta);

                // mousedown (not click) fires before the text input's blur,
                // so the suggestion list is still open when this runs.
                item.addEventListener('mousedown', function (evt) {
                    evt.preventDefault();
                    addChip(opt.id);
                });
                item.addEventListener('mouseenter', function () {
                    highlightIndex = index;
                    updateHighlight();
                });

                suggestionsEl.appendChild(item);
            });

            openSuggestions();
        }

        textInput.addEventListener('input', function () {
            renderSuggestions(textInput.value);
        });

        textInput.addEventListener('focus', function () {
            renderSuggestions(textInput.value);
        });

        textInput.addEventListener('keydown', function (evt) {
            if (evt.key === 'ArrowDown') {
                evt.preventDefault();
                if (suggestionsEl.hidden) { renderSuggestions(textInput.value); return; }
                if (visibleMatches.length === 0) return;
                highlightIndex = (highlightIndex + 1) % visibleMatches.length;
                updateHighlight();
            } else if (evt.key === 'ArrowUp') {
                evt.preventDefault();
                if (visibleMatches.length === 0) return;
                highlightIndex = (highlightIndex - 1 + visibleMatches.length) % visibleMatches.length;
                updateHighlight();
            } else if (evt.key === 'Enter') {
                if (!suggestionsEl.hidden && highlightIndex >= 0 && visibleMatches[highlightIndex]) {
                    evt.preventDefault();
                    addChip(visibleMatches[highlightIndex].id);
                }
            } else if (evt.key === 'Escape') {
                closeSuggestions();
            } else if (evt.key === 'Backspace' && textInput.value === '' && selectedIds.length > 0) {
                removeChip(selectedIds[selectedIds.length - 1]);
            } else if (evt.key === ',') {
                if (visibleMatches.length > 0) {
                    evt.preventDefault();
                    addChip(visibleMatches[highlightIndex >= 0 ? highlightIndex : 0].id);
                }
            }
        });

        document.addEventListener('click', function (evt) {
            if (!root.contains(evt.target)) closeSuggestions();
        });

        renderChips();
    }

    function init() {
        var roots = document.querySelectorAll('[data-chip-input]');
        Array.prototype.forEach.call(roots, initChipInput);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
