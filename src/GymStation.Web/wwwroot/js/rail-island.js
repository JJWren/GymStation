// The rail's slot height is rem-based under the viewport type clamp — measure,
// never assume (#96).
export function slotPx(grid) {
    const raw = getComputedStyle(grid).getPropertyValue("--slot-h");
    return parseFloat(raw) * parseFloat(getComputedStyle(document.documentElement).fontSize);
}
