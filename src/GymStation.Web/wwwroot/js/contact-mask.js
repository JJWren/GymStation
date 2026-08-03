// Round-4 #138: as-you-type US phone mask, exactly as specced — "2" becomes
// "(2", the third digit closes to "(251) ", the seventh adds the dash. The
// server normalizes to digits regardless, so no-JS typing works fine too.
(function () {
    function format(digits) {
        if (digits.length === 0) { return ""; }
        if (digits.length <= 3) { return "(" + digits + (digits.length === 3 ? ") " : ""); }
        if (digits.length <= 6) { return "(" + digits.slice(0, 3) + ") " + digits.slice(3); }
        return "(" + digits.slice(0, 3) + ") " + digits.slice(3, 6) + "-" + digits.slice(6, 10);
    }

    function init(input) {
        if (input.dataset.maskReady) { return; }
        input.dataset.maskReady = "1";
        input.addEventListener("input", function () {
            var digits = input.value.replace(/\D/g, "").slice(0, 10);
            input.value = format(digits);
        });
    }

    function initAll() {
        document.querySelectorAll("input[data-phone-mask]").forEach(init);
    }

    document.addEventListener("DOMContentLoaded", initAll);

    // Blazor enhanced navigation swaps the DOM without re-running scripts.
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener("enhancedload", initAll);
    } else {
        document.addEventListener("DOMContentLoaded", function () {
            if (window.Blazor && window.Blazor.addEventListener) {
                window.Blazor.addEventListener("enhancedload", initAll);
            }
        });
    }
})();
