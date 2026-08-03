// Round-4 #127: pencil -> inline edit. The server renders the editable form
// (that IS the no-JS experience); with JS the read-only view swaps in, the
// pencil opens editing, save posts the normal form (PRG), cancel/Esc reverts
// client-side without a request.
(function () {
    function init(form) {
        if (form.dataset.ieReady) { return; }
        form.dataset.ieReady = '1';

        var view = form.querySelector('.ie-view');
        var fields = form.querySelector('.ie-fields');
        var cancel = form.querySelector('.ie-cancel');
        var edit = form.querySelector('.ie-edit');
        var inputs = fields ? Array.prototype.slice.call(fields.querySelectorAll('input:not([type=hidden])')) : [];
        if (!view || !fields || !edit || inputs.length === 0) { return; }

        function show(editing) {
            view.hidden = editing;
            fields.hidden = !editing;
            if (editing) { inputs[0].focus(); inputs[0].select(); }
        }

        // Reverting must hand keyboard focus back to the pencil — hiding the
        // focused input would otherwise drop focus to <body>.
        function revert() {
            inputs.forEach(function (input) { input.value = input.defaultValue; });
            show(false);
            edit.focus();
        }

        if (cancel) {
            cancel.hidden = false;
            cancel.addEventListener('click', revert);
        }
        edit.addEventListener('click', function () { show(true); });
        inputs.forEach(function (input) {
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); revert(); }
            });
        });
        show(false);
    }

    function initAll() {
        document.querySelectorAll('form[data-inline-edit]').forEach(init);
    }

    document.addEventListener('DOMContentLoaded', initAll);

    // Blazor enhanced navigation swaps the DOM without re-running scripts.
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', initAll);
    } else {
        document.addEventListener('DOMContentLoaded', function () {
            if (window.Blazor && window.Blazor.addEventListener) {
                window.Blazor.addEventListener('enhancedload', initAll);
            }
        });
    }
})();
