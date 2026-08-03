// The rail's slot height is rem-based under the viewport type clamp — measure,
// never assume (#96).
export function slotPx(grid) {
    const raw = getComputedStyle(grid).getPropertyValue("--slot-h");
    return parseFloat(raw) * parseFloat(getComputedStyle(document.documentElement).fontSize);
}

// Horizontal day step for cross-day dragging (#131): the delta between two
// adjacent columns' left edges, so the grid's column-gap is included — a bare
// column width would snap day changes early.
export function dayPx(grid) {
    const cols = grid.querySelectorAll(".rail-col");
    if (cols.length >= 2) {
        return cols[1].getBoundingClientRect().left - cols[0].getBoundingClientRect().left;
    }
    return cols.length === 1 ? cols[0].getBoundingClientRect().width : 1;
}

// Capture the pointer on the grid for the whole gesture: pointerup then fires
// on the grid even when released outside it, so a drag can never strand.
export function capture(grid, pointerId) {
    try { grid.setPointerCapture(pointerId); } catch { /* pointer already gone */ }
}

// Viewport-space grid edges for the edge-hover week paging zones (#132) —
// measured at drag start because layout can shift between gestures.
export function gridRect(grid) {
    const r = grid.getBoundingClientRect();
    return { left: r.left, right: r.right };
}
