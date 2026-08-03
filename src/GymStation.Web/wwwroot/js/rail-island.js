// The rail's slot height is rem-based under the viewport type clamp — measure,
// never assume (#96).
export function slotPx(grid) {
    const raw = getComputedStyle(grid).getPropertyValue("--slot-h");
    return parseFloat(raw) * parseFloat(getComputedStyle(document.documentElement).fontSize);
}

// Day-column width for cross-day dragging (#131) — measured off a real column
// for the same reason.
export function dayPx(grid) {
    const col = grid.querySelector(".rail-col");
    return col ? col.getBoundingClientRect().width : 1;
}
