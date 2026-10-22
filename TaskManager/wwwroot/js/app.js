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

// ── Hint mode: press Ctrl+Alt to show keyboard shortcuts for every clickable element, Ctrl+Alt again (or Esc) to hide ──
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

    const matches = [...document.querySelectorAll(
      "button, a[href], input:not([type=hidden]), select, textarea, " +
      "[onclick], .menu-item, .chip.x, .nav-btn, .row-title, .log-e, .card, " +
      "header.col-h, .seg-o, label.check input"
    )].filter(el => this._spot(el));   // only what's on screen: not scrolled out of a board column or hidden under the side menu
    // The side nav's own buttons go first, in their fixed markup order, so they always land on the same
    // letters regardless of how much page content (cards, rows, …) is hinted alongside them.
    const nav = matches.filter(el => el.closest("nav.side"));
    const page = matches.filter(el => !el.closest("nav.side"));
    const els = [...nav, ...page];

    const labels = this._labels(els.length);
    this.map.clear();
    els.forEach((el, i) => {
      const label = labels[i];
      this.map.set(label, el);
      const badge = document.createElement("span");
      badge.className = "hint-badge";
      badge.textContent = label;
      badge.dataset.hint = label;
      const [x, y] = this._spot(el);
      badge.style.left = `${x + scrollX}px`;
      badge.style.top = `${y + scrollY}px`;
      this.overlay.appendChild(badge);
    });
  },

  // A point where the element is actually visible (bottom-right corner preferred, for the badge), or null when
  // it's clipped by a scroll area, covered by something else, or off screen.
  _spot(el) {
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return null;
    const corner = [[r.right - 4, r.bottom - 4], [r.right - 14, r.bottom - 14]];   // the second clears rounded corners
    const inside = [[.5, .5], [.1, .5], [.9, .5], [.5, .1], [.5, .9]].map(([fx, fy]) => [r.left + r.width * fx, r.top + r.height * fy]);
    for (const [i, [x, y]] of [...corner, ...inside].entries()) {
      if (x < 0 || y < 0 || x >= innerWidth || y >= innerHeight) continue;
      const hit = document.elementFromPoint(x, y);
      if (hit && (hit === el || el.contains(hit))) return i < corner.length ? [r.right - 6, r.bottom - 6] : [x, y];
    }
    return null;
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
  // Ctrl+Alt in either order: the second key's keydown carries the first as a modifier.
  if ((e.key === "Alt" && e.ctrlKey) || (e.key === "Control" && e.altKey)) {
    e.preventDefault();
    if (e.repeat) return;   // ignore OS auto-repeat while the keys are held down
    if (_hints.active) _hints.hide(); else _hints.show();
    return;
  }
  if (_hints.active && e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
    e.preventDefault();
    _hints.type(e.key);
  }
}, true);

// Edge fades on scroll areas (.scx): on only while content continues past the right / bottom edge.
// Checked on scroll, resize, and whenever Blazor changes the DOM (one check per frame at most).
let _fadeQueued = false;
function _updateFades() {
  _fadeQueued = false;
  for (const box of document.querySelectorAll(".scx")) {
    const s = box.firstElementChild;
    box.classList.toggle("more-right", !!s && s.scrollLeft + s.clientWidth < s.scrollWidth - 1);
    box.classList.toggle("more-bottom", !!s && s.scrollTop + s.clientHeight < s.scrollHeight - 1);
  }
}
function _queueFades() { if (!_fadeQueued) { _fadeQueued = true; requestAnimationFrame(_updateFades); } }
document.addEventListener("scroll", _queueFades, true);
addEventListener("resize", _queueFades);
new MutationObserver(_queueFades).observe(document.documentElement, { childList: true, subtree: true });

// HTML5 drag-and-drop: a drop only happens where dragover is cancelled. Blazor can't do that without a .NET
// round-trip per mouse move, so board columns/lanes are marked data-drop and cancelled here.
document.addEventListener("dragover", (e) => {
  if (e.target.closest?.("[data-drop]")) { e.preventDefault(); e.dataTransfer.dropEffect = "move"; }
});
document.addEventListener("dragstart", (e) => { if (e.target.closest?.(".card, .col-h, .cat-h")) e.dataTransfer.effectAllowed = "move"; });
