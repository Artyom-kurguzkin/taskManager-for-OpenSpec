// Theme switch, OS dark-mode watch, menu placement, and keyboard handling (Esc).
window.rtm = {
  setTheme(theme) {
    const root = document.documentElement;
    if (theme === "light" || theme === "dark") root.dataset.theme = theme;
    else delete root.dataset.theme;
  },

  // Returns whether the OS is in dark mode now, and tells .NET (MainLayout.SystemDarkChanged) when it changes.
  watchDark(dotnet) {
    const q = matchMedia("(prefers-color-scheme: dark)");
    q.addEventListener("change", (e) => dotnet.invokeMethodAsync("SystemDarkChanged", e.matches));
    return q.matches;
  },

  // Puts a menu under its activator (flipping above / left when it would leave the window).
  // position: fixed, so scrolling containers such as the board can't clip it.
  place(anchor, pop) {
    if (!anchor || !pop) return;
    const r = anchor.getBoundingClientRect();
    const w = pop.offsetWidth, h = pop.offsetHeight;
    let x = r.left, y = r.bottom + 6;
    if (x + w > innerWidth - 8) x = Math.max(8, r.right - w);
    if (y + h > innerHeight - 8) y = Math.max(8, r.top - h - 6);
    pop.style.left = `${x}px`;
    pop.style.top = `${y}px`;
    pop.classList.add("placed");
    pop.querySelector(".menu-item.cur, .menu-item")?.focus();
  },
};

document.addEventListener("keydown", (e) => {
  // Arrow keys move between items of an open menu.
  const pop = document.querySelector(".menu-pop");
  if (pop && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
    const items = [...pop.querySelectorAll(".menu-item")], i = items.indexOf(document.activeElement);
    items[(i + (e.key === "ArrowDown" ? 1 : -1) + items.length) % items.length]?.focus();
    e.preventDefault();
    return;
  }
  if (e.key !== "Escape") return;
  if (pop) { document.querySelector(".menu-backdrop")?.click(); return; }                     // close the open menu
  const cancel = document.querySelector(".dialog .dialog-cancel");
  if (cancel) { cancel.click(); return; }                                                      // cancel the open dialog
  if (e.target.closest?.("input, textarea, [contenteditable]")) { e.target.blur(); return; }  // Esc first leaves the field
  if (location.pathname !== "/") history.back();
});

// HTML5 drag-and-drop: a drop only happens where dragover is cancelled. Blazor can't do that without a .NET
// round-trip per mouse move, so board columns/lanes are marked data-drop and cancelled here.
document.addEventListener("dragover", (e) => {
  if (e.target.closest?.("[data-drop]")) { e.preventDefault(); e.dataTransfer.dropEffect = "move"; }
});
document.addEventListener("dragstart", (e) => { if (e.target.closest?.(".card")) e.dataTransfer.effectAllowed = "move"; });
