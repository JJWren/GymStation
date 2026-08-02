// Opt-in idle sign-out (#86). The shell root carries data-idle-minutes only when
// the signed-in account enabled it; the timer submits the page's rendered logout
// form so the POST rides the real antiforgery token. Deadline math is absolute —
// a background tab's throttled interval still signs out on its next tick.
(() => {
    const root = document.querySelector("[data-idle-minutes]");
    if (!root) return;
    const minutes = parseInt(root.getAttribute("data-idle-minutes"), 10);
    if (!minutes || minutes <= 0) return;

    const limit = minutes * 60000;
    let last = Date.now();
    const bump = () => { last = Date.now(); };
    for (const ev of ["pointerdown", "keydown", "wheel", "touchstart", "scroll"]) {
        addEventListener(ev, bump, { passive: true, capture: true });
    }

    const timer = setInterval(() => {
        if (Date.now() - last >= limit) {
            // One shot: a slow network must not stack repeat submits every tick.
            clearInterval(timer);
            const form = document.querySelector('form[action="/auth/logout"]');
            if (form) form.submit();
        }
    }, 15000);
})();
