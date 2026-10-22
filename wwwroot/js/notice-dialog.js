// ==========================================================================
// Shared confirm/notice dialog — a styled replacement for window.confirm()/
// alert(), generalized from the one Records already built and proved out
// (wwwroot/js/records.js). Any page that includes this file plus
// Views/Shared/_NoticeDialog.cshtml and notice-dialog.css gets
// window.showNotice(opts) for free.
//
// showNotice({ tone, icon, title, file, message, steps, note, okText, cancelText })
// resolves true when OK is pressed, false when cancelled/closed/backdrop-
// clicked. Leave cancelText out for a message with a single OK button
// (an alert() replacement rather than a confirm() replacement).
// ==========================================================================
(function () {
    var noticeDialog = document.getElementById('notice-dialog');

    function showNotice(opts) {
        if (!noticeDialog) {
            // Fallback if the dialog markup is missing from this page.
            var text = [opts.title, opts.message].filter(Boolean).join('\n\n');
            return Promise.resolve(opts.cancelText ? confirm(text) : (alert(text), true));
        }

        var el = function (id) { return document.getElementById(id); };
        var setText = function (id, value) {
            var node = el(id);
            node.textContent = value || '';
            node.hidden = !value;
        };

        noticeDialog.dataset.tone = opts.tone || 'info';
        el('notice-icon').textContent = opts.icon || 'info';
        el('notice-title').textContent = opts.title || '';
        setText('notice-file', opts.file);
        setText('notice-message', opts.message);
        setText('notice-note', opts.note);

        var steps = el('notice-steps');
        steps.innerHTML = '';
        (opts.steps || []).forEach(function (step, i) {
            var li = document.createElement('li');
            var num = document.createElement('span');
            num.className = 'notice-step-num';
            num.textContent = String(i + 1);
            li.appendChild(num);
            li.appendChild(document.createTextNode(step));
            steps.appendChild(li);
        });
        steps.hidden = !(opts.steps && opts.steps.length);

        var okBtn = el('notice-ok');
        var cancelBtn = el('notice-cancel');
        okBtn.textContent = opts.okText || 'OK';
        cancelBtn.textContent = opts.cancelText || 'Cancel';
        cancelBtn.hidden = !opts.cancelText;

        return new Promise(function (resolve) {
            var result = false;
            var finish = function (value) { result = value; noticeDialog.close(); };
            var onOk = function () { finish(true); };
            var onCancel = function () { finish(false); };
            var onBackdrop = function (e) { if (e.target === noticeDialog) finish(false); };
            var onClose = function () {
                okBtn.removeEventListener('click', onOk);
                cancelBtn.removeEventListener('click', onCancel);
                noticeDialog.removeEventListener('click', onBackdrop);
                resolve(result);
            };
            okBtn.addEventListener('click', onOk);
            cancelBtn.addEventListener('click', onCancel);
            noticeDialog.addEventListener('click', onBackdrop);
            noticeDialog.addEventListener('close', onClose, { once: true });
            noticeDialog.showModal();
            okBtn.focus();
        });
    }

    window.showNotice = showNotice;
})();
