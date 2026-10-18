// Theme switch (tokens live on html[data-theme]) and Esc-to-go-back, as in the agreed mockup.
window.rtm = {
  setTheme(theme) {
    const root = document.documentElement;
    if (theme === "light" || theme === "dark") root.dataset.theme = theme;
    else delete root.dataset.theme;
  },
};

document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  if (e.target.closest?.("input, textarea, [contenteditable]")) { e.target.blur(); return; }   // Esc first leaves the field
  if (document.querySelector(".mud-popover-open, .mud-dialog")) return;                       // MudBlazor closes its own popups
  if (location.pathname !== "/") history.back();
});

// HTML5 drag-and-drop: a drop only happens where dragover is cancelled. Blazor can't do that without a .NET
// round-trip per mouse move, so board columns/lanes are marked data-drop and cancelled here.
document.addEventListener("dragover", (e) => {
  if (e.target.closest?.("[data-drop]")) { e.preventDefault(); e.dataTransfer.dropEffect = "move"; }
});
document.addEventListener("dragstart", (e) => { if (e.target.closest?.(".card")) e.dataTransfer.effectAllowed = "move"; });
