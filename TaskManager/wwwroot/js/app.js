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
