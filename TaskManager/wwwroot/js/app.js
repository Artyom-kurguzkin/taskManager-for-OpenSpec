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
    (pop.querySelector(".menu-filter") ?? pop.querySelector(".menu-item.cur, .menu-item"))?.focus();   // pickers: type to filter
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
  if (_hints.active) { _hints.hide(); return; }
  if (pop) { document.querySelector(".menu-backdrop")?.click(); return; }                     // close the open menu
  const cancel = document.querySelector(".dialog .dialog-cancel");
  if (cancel) { cancel.click(); return; }                                                      // cancel the open dialog
  if (e.target.closest?.("input, textarea, [contenteditable]")) { e.target.blur(); return; }  // Esc first leaves the field
  if (location.pathname !== "/") history.back();
});

// ── Hint mode: hold Alt to see keyboard shortcuts for every clickable element ──
const _hints = {
  active: false,
  overlay: null,   // container div
  map: new Map(),  // label → element
  typed: "",

  show() {
    if (this.active) return;
    this.active = true;
    this.typed = "";
    this.overlay = document.createElement("div");
    this.overlay.className = "hint-overlay";
    document.body.appendChild(this.overlay);

    const els = [...document.querySelectorAll(
      "button, a[href], input:not([type=hidden]), select, textarea, " +
      "[onclick], .menu-item, .chip.x, .nav-btn, .row-title, .log-e, .card, " +
      "header.col-h, .seg-o, label.check input"
    )].filter(el => {
      if (el.offsetParent === null && !el.closest("nav.side")) return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    });

    const labels = this._labels(els.length);
    this.map.clear();
    els.forEach((el, i) => {
      const label = labels[i];
      this.map.set(label, el);
      const badge = document.createElement("span");
      badge.className = "hint-badge";
      badge.textContent = label;
      badge.dataset.hint = label;
      const r = el.getBoundingClientRect();
      badge.style.left = `${r.right - 6 + scrollX}px`;
      badge.style.top = `${r.bottom - 6 + scrollY}px`;
      this.overlay.appendChild(badge);
    });
  },

  hide() {
    this.active = false;
    this.typed = "";
    this.overlay?.remove();
    this.overlay = null;
    this.map.clear();
  },

  type(ch) {
    this.typed += ch.toLowerCase();
    // dim non-matching badges
    for (const badge of this.overlay?.children ?? []) {
      badge.classList.toggle("dim", !badge.dataset.hint.startsWith(this.typed));
    }
    const el = this.map.get(this.typed);
    if (el) { this.hide(); el.click(); el.focus?.(); return; }
    // if nothing can match, reset
    if (![...this.map.keys()].some(k => k.startsWith(this.typed))) {
      this.typed = "";
      for (const badge of this.overlay?.children ?? []) badge.classList.remove("dim");
    }
  },

  _labels(n) {
    const chars = "asdfghjklqwertyuiopzxcvbnm";
    if (n <= chars.length) return [...chars.slice(0, n)];
    const out = [];
    for (let i = 0; i < n; i++) {
      out.push(chars[Math.floor(i / chars.length) % chars.length] + chars[i % chars.length]);
    }
    return out;
  },
};

document.addEventListener("keydown", (e) => {
  if (e.key === "Alt") { e.preventDefault(); _hints.show(); return; }
  if (_hints.active && e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
    e.preventDefault();
    _hints.type(e.key);
  }
}, true);

document.addEventListener("keyup", (e) => {
  if (e.key === "Alt" && _hints.active && _hints.typed === "") _hints.hide();
});

// HTML5 drag-and-drop: a drop only happens where dragover is cancelled. Blazor can't do that without a .NET
// round-trip per mouse move, so board columns/lanes are marked data-drop and cancelled here.
document.addEventListener("dragover", (e) => {
  if (e.target.closest?.("[data-drop]")) { e.preventDefault(); e.dataTransfer.dropEffect = "move"; }
});
document.addEventListener("dragstart", (e) => { if (e.target.closest?.(".card, .col-h, .cat-h")) e.dataTransfer.effectAllowed = "move"; });
