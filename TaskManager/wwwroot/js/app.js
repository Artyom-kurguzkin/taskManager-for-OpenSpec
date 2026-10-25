// Theme switch, OS dark-mode watch, menu placement, and keyboard handling (Esc).
window.rtm = {
  // Same breakpoint as the work item page's one-column bento in app.css: there it opens with every tile minimised.
  isNarrow() { return matchMedia("(max-width: 860px)").matches; },

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
  if (e.target.closest?.("[data-own-esc]")) return;                                            // the field cancels itself (subtask editor)
  if (e.target.closest?.("input, textarea, [contenteditable]")) { e.target.blur(); return; }  // Esc first leaves the field
  if (location.pathname !== "/") history.back();
});

// ── Hint mode: press F (outside text fields) to show keyboard shortcuts for every clickable element, F again (or Esc) to hide ──
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
      "header.col-h, .seg-o, label.check input, .sub .s-t"
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

  // shift: Shift held on the key that completes the label. That Shift-clicks the element (selects a card or subtask, as
  // a Shift-click with the mouse does) and keeps the badges up, so several can be picked in a row.
  type(ch, shift) {
    this.typed += ch.toLowerCase();
    // dim non-matching badges
    for (const badge of this.overlay?.children ?? []) {
      badge.classList.toggle("dim", !badge.dataset.hint.startsWith(this.typed));
    }
    const el = this.map.get(this.typed);
    if (el && shift) {
      el.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, shiftKey: true, view: window }));
      this.typed = "";
      for (const badge of this.overlay?.children ?? []) badge.classList.remove("dim");
      return;
    }
    if (el) { this.hide(); el.click(); el.focus?.(); return; }
    // if nothing can match, reset
    if (![...this.map.keys()].some(k => k.startsWith(this.typed))) {
      this.typed = "";
      for (const badge of this.overlay?.children ?? []) badge.classList.remove("dim");
    }
  },

  _labels(n) {
    const chars = "asdghjklqwertyuiopzxcvbnm";   // no f: F toggles hint mode
    if (n <= chars.length) return [...chars.slice(0, n)];
    const out = [];
    for (let i = 0; i < n; i++) {
      out.push(chars[Math.floor(i / chars.length) % chars.length] + chars[i % chars.length]);
    }
    return out;
  },
};

/** True while the cursor is in something you type into: F (and badge letters) must reach it untouched. */
function _typing(el) {
  if (!el?.closest) return false;
  if (el.closest("textarea, select, [contenteditable]:not([contenteditable=false])")) return true;
  return el.matches("input") && !["checkbox", "radio", "button", "submit", "reset", "range", "color", "file"].includes(el.type);
}

document.addEventListener("keydown", (e) => {
  // F toggles the hints (never a badge letter, see _labels); in a text field it types as usual.
  if (e.key.toLowerCase() === "f" && !e.ctrlKey && !e.altKey && !e.metaKey && (_hints.active || !_typing(e.target))) {
    e.preventDefault();
    if (e.repeat) return;   // ignore OS auto-repeat while F is held down
    if (_hints.active) _hints.hide(); else _hints.show();
    return;
  }
  if (_hints.active && e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
    e.preventDefault();
    _hints.type(e.key, e.shiftKey);
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

// ── Board drag (pointer events + FLIP) ──
// Native HTML5 drag-and-drop gives only a ghost image and throttled dragover events, so the board drags with pointer
// events the way react-beautiful-dnd and dnd-kit do: the real card (or column) follows the pointer, the cards it passes
// slide aside with transforms only (no layout work per move), and after the drop every card animates from where it was
// to where Blazor re-rendered it (FLIP: first, last, invert, play). Blazor's @ondragstart / @ondrop handlers still decide
// the outcome; they get synthetic events, the drop sent to what sits right after the gap: a card or column means
// "in front of this one", the lane / column / row itself (all marked data-drop) means "at the end".
const EASE = "cubic-bezier(.2,0,0,1)", MS = 200, GRAB = 6;
let _press = null;    // pointer is down on a card or column header; becomes a drag after GRAB px
let _drag = null;     // the drag in progress: see _startDrag
let _settling = null; // a dropped drag kept frozen until Blazor re-renders
let _flip = null;     // last frame's rects of every card and column, by data-id / data-key, while a re-render is expected
let _flipUntil = 0;
let _swallowClick = false;

function _rect(el) { return el.getBoundingClientRect(); }
/** Where layout put an element: its rect with the current (possibly mid-animation) transform taken back out. */
function _base(el) {
  const r = _rect(el), m = new DOMMatrixReadOnly(getComputedStyle(el).transform);
  return { top: r.top - m.f, bottom: r.bottom - m.f, left: r.left - m.e, right: r.right - m.e };
}
function _key(el) { return el.dataset.row !== undefined ? "r" + el.dataset.row : el.dataset.id !== undefined ? "i" + el.dataset.id : el.dataset.tile !== undefined ? "t" + el.dataset.tile : "k" + el.dataset.key; }
const _FLIPPED = ".lane > .card[data-id], section.col[data-key], tr[data-row], .tile[data-tile]";   // board cards and columns, list rows, work item page tiles
function _snapshot() {
  const m = new Map();
  for (const el of document.querySelectorAll(_FLIPPED)) m.set(_key(el), _rect(el));
  return m;
}
/** Keeps a fresh snapshot for a while, so the next board re-render can animate from the last frame. */
function _armFlip() {
  const first = performance.now() >= _flipUntil;
  _flipUntil = performance.now() + 1000;
  if (first) (function tick() { _flip = _snapshot(); if (performance.now() < _flipUntil) requestAnimationFrame(tick); else _flip = null; })();
}
function _synthetic(type) { return new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() }); }

document.addEventListener("pointerdown", (e) => {
  if (e.button === 0 && e.target.closest(".tile:not(.focus), .tile-h")) _armFlip();   // focusing or minimising a work item tile: slide the tiles to their new places
  if (e.button !== 0 || e.target.closest("input, button, a, select, textarea, .pop")) return;
  const card = e.target.closest(".lane > .card:not(.subcard)"), head = !card && e.target.closest(".col-h[data-drag-col]");   // Blazor renders a true bool as a bare attribute
  const grip = !card && !head && e.target.closest("tr[data-id] .grip");   // list rows drag by their handle, so dragging across cells can select them instead
  if (!card && !head && !grip) return;
  _press = { handle: card || head || grip, el: card || (head ? head.closest("section.col") : grip.closest("tr")), kind: card ? "card" : head ? "col" : "row", x: e.clientX, y: e.clientY, pointer: e.pointerId };
});

document.addEventListener("pointermove", (e) => {
  if (_press && !_drag && e.pointerId === _press.pointer && Math.hypot(e.clientX - _press.x, e.clientY - _press.y) >= GRAB) _startDrag(e);
  const d = _drag;
  if (!d || e.pointerId !== d.pointer) return;
  _autoscroll(d, e);
  const dx = e.clientX - d.x + d.scroller.scrollLeft - d.sx, dy = d.kind === "col" ? 0 : e.clientY - d.y + d.scroller.scrollTop - d.sy;
  d.el.style.transform = `translate(${dx}px, ${dy}px)` + (d.kind === "card" ? " scale(1.02)" : "");
  d.spot = _findSpot(d, e) ?? d.spot;   // over nothing: keep the last gap
  _layoutGap(d);
});

function _startDrag(e) {
  const p = _press; _press = null;
  const box = p.el.parentElement, scroller = p.el.closest(".board-wrap, .tbl-wrap");
  const picked = p.kind !== "col" && p.el.classList.contains("selected") ? [...document.querySelectorAll(p.kind === "card" ? ".lane > .card.selected" : "tr[data-id][data-selected]")] : [];
  const r = _rect(p.el), axis = p.kind === "col" ? "X" : "Y";
  const d = _drag = {
    ...p, box, scroller, sx: scroller.scrollLeft, sy: scroller.scrollTop, axis, spot: null, shifted: new Map(),
    els: picked.length > 1 ? picked : [p.el],   // a selected card carries the whole selection (BoardPage.StartCardDrag agrees)
    size: (axis === "Y" ? r.height : r.width) + (parseFloat(getComputedStyle(box)[axis === "Y" ? "rowGap" : "columnGap"]) || 0),
  };
  d.i0 = _items(d, box).findIndex(c => p.el.compareDocumentPosition(c) & Node.DOCUMENT_POSITION_FOLLOWING);   // its place among the others
  if (d.i0 < 0) d.i0 = _items(d, box).length;
  p.el.setPointerCapture?.(e.pointerId);
  p.handle.dispatchEvent(_synthetic("dragstart"));   // BoardPage records what is moving
  Object.assign(p.el.style, { transition: "none", zIndex: "5", pointerEvents: "none", boxShadow: p.kind === "card" ? "6px 6px 0 var(--shadow)" : "" });
  if (p.kind === "row") { p.el.style.position = "relative"; for (const td of p.el.cells) td.style.background = "var(--card)"; }   // z-index needs position; cells are see-through
  for (const el of d.els) if (el !== p.el) el.style.opacity = ".35";
  document.body.classList.add("is-dragging");
}

/** The other cards in a lane, the other columns in a row, or every other row of the list (group, item, detail and placeholder rows), in layout order. */
function _items(d, box) { return [...box.children].filter(c => c !== d.el && c.matches(d.kind === "card" ? ".card:not(.subcard)" : d.kind === "col" ? "section.col" : "tr")); }

/** The box under the pointer and the item the drag would land in front of (null = at the end). */
function _findSpot(d, e) {
  const under = document.elementFromPoint(e.clientX, e.clientY);
  let box;
  if (d.kind === "card") {
    const lanes = [...(under?.closest("section.col")?.querySelectorAll(".lane[data-drop]") ?? [])];
    box = lanes.find(l => e.clientY < _base(l).bottom) ?? lanes.at(-1);   // over the header: the first lane
  } else if (d.kind === "row") box = under?.closest("tbody");           // the list's one body: its groups are rows in it
  else if (under?.closest(".cat-cols") === d.box) box = d.box;          // columns stay in their own row
  if (!box) return null;
  const items = _items(d, box), p = d.axis === "Y" ? e.clientY : e.clientX;
  let before = items.find(el => { const b = _base(el); return p < (d.axis === "Y" ? b.top + b.bottom : b.left + b.right) / 2; }) ?? null;
  let target = before;
  if (d.kind === "row") {
    while (before && !before.matches("tr[data-id], tr[data-group], tr[data-empty]")) before = items[items.indexOf(before) + 1] ?? null;   // never between a row and its subtasks
    // the drop goes to the row it lands in front of; at the end of a group (before the next group's row, or the table's end) to that group's row
    target = before?.matches("tr[data-id], tr[data-empty]") ? before
      : items.slice(0, before ? items.indexOf(before) : items.length).reverse().find(el => el.matches("tr[data-group]")) ?? before;
  }
  return { box, items, before, target };
}

/** Slides items aside: the hole the dragged item left closes behind it, and a gap its size opens at the spot. */
function _layoutGap(d) {
  const s = d.spot, want = new Map();
  const at = s ? (s.before ? s.items.indexOf(s.before) : s.items.length) : -1;
  if (s) s.items.forEach((el, i) => want.set(el, (i >= at ? d.size : 0) - (s.box === d.box && i >= d.i0 ? d.size : 0)));
  if (!s || s.box !== d.box) _items(d, d.box).forEach((el, i) => { if (i >= d.i0) want.set(el, -d.size); });
  for (const el of d.shifted.keys()) if (!want.has(el)) want.set(el, 0);
  for (const [el, v] of want) {
    if ((d.shifted.get(el) ?? 0) === v) continue;
    el.style.transition = `transform ${MS}ms ${EASE}`;
    el.style.transform = v ? `translate${d.axis}(${v}px)` : "";
    v ? d.shifted.set(el, v) : d.shifted.delete(el);
  }
}

function _autoscroll(d, e) {
  const r = _rect(d.scroller), m = 40, step = 12;
  if (e.clientX < r.left + m) d.scroller.scrollLeft -= step; else if (e.clientX > r.right - m) d.scroller.scrollLeft += step;
  if (d.kind !== "col") { if (e.clientY < r.top + m) d.scroller.scrollTop -= step; else if (e.clientY > r.bottom - m) d.scroller.scrollTop += step; }
}

/** Puts everything back as it was, sliding (a cancelled drag) or at once (the board is about to show the new order). */
function _restore(d, slide) {
  clearTimeout(d.giveUp);
  if (_settling === d) _settling = null;
  const els = [d.el, ...d.els, ...d.shifted.keys()];
  for (const el of els) {
    el.style.transition = slide ? `transform ${MS}ms ${EASE}, opacity ${MS}ms` : "none";
    Object.assign(el.style, { transform: "", opacity: "", zIndex: "", pointerEvents: "", boxShadow: "" });
  }
  if (d.kind === "row") { d.el.style.position = ""; for (const td of d.el.cells) td.style.background = ""; }
  setTimeout(() => els.forEach(el => el.style.transition = ""), MS + 50);
  document.body.classList.remove("is-dragging");
}

document.addEventListener("pointerup", (e) => {
  _press = null;
  const d = _drag;
  if (!d || e.pointerId !== d.pointer) return;
  _drag = null;
  _swallowClick = true; setTimeout(() => _swallowClick = false);   // the click that follows a drag is not a click on the card
  const s = d.spot, at = s ? (s.before ? s.items.indexOf(s.before) : s.items.length) : -1;
  if (!s || (s.box === d.box && at === d.i0)) { _restore(d, true); return; }   // nowhere, or back where it started
  _armFlip();
  (s.target ?? s.before ?? s.box).dispatchEvent(_synthetic("drop"));
  _settling = d;   // hold the gap until Blazor re-renders, then FLIP takes over from exactly here
  d.giveUp = setTimeout(() => _restore(d, true), 800);   // the drop changed nothing (refused, or archive declined)
});
document.addEventListener("pointercancel", () => { if (_drag) { _restore(_drag, true); _drag = null; } _press = null; });
document.addEventListener("click", (e) => { if (_swallowClick) { e.stopPropagation(); e.preventDefault(); } }, true);

// Holding Shift is select mode: the board shows its hint bar and cards show they can be picked (css: body.shift-held).
document.addEventListener("keydown", (e) => { if (e.key === "Shift") document.body.classList.add("shift-held"); });
document.addEventListener("keyup", (e) => { if (e.key === "Shift") document.body.classList.remove("shift-held"); });
addEventListener("blur", () => document.body.classList.remove("shift-held"));

// Capture phase: the list's grid handles arrow keys itself when it has the focus, so the app takes them first.
addEventListener("keydown", (e) => {
  if (!e.isTrusted) return;   // the key re-sent below
  if (e.key === "Escape" && _drag) { _restore(_drag, true); _drag = null; return; }
  if (!e.key.startsWith("Arrow") || _typing(e.target) || e.target.closest(".pop")) return;
  // BoardPage.KeyDown, on the view's wrapper, moves the selected cards / checked rows. The key is re-sent there whatever has
  // the focus (a checkbox, the grid, the body after a hint-mode pick), and neither the page nor the grid may act on it as well.
  const wrap = document.querySelector(".board-wrap, .tbl-wrap");
  if (!wrap || !document.querySelector(".lane > .card.selected, tr[data-id][data-selected]")) return;
  e.preventDefault(); e.stopPropagation(); _armFlip();
  wrap.dispatchEvent(new KeyboardEvent("keydown", { key: e.key, bubbles: true }));
}, true);

// After a drop or an arrow move, Blazor re-renders the board: animate every card and column from its last frame.
// Style attributes are ignored: those are this script's own writes (reacting to them would loop forever).
new MutationObserver((records) => {
  if (!_flip) return;
  const board = records.some(r => !(r.type === "attributes" && r.attributeName === "style") &&
    (r.target.nodeType === 1 ? r.target : r.target.parentElement)?.closest?.(".board, .tbl-wrap, .bento"));
  if (!board) return;
  const was = _flip;
  if (_settling) _restore(_settling, false);
  const moves = [];
  for (const el of document.querySelectorAll(_FLIPPED)) {
    const o = was.get(_key(el)); if (!o) continue;
    const n = _rect(el), dx = o.left - n.left, dy = o.top - n.top;
    if (Math.abs(dx) > .5 || Math.abs(dy) > .5) moves.push([el, dx, dy]);
  }
  for (const [el, dx, dy] of moves) { el.style.transition = "none"; el.style.transform = `translate(${dx}px, ${dy}px)`; }
  void document.body.offsetHeight;   // commit the inverted positions, then play
  for (const [el] of moves) { el.style.transition = `transform ${MS}ms ${EASE}`; el.style.transform = ""; }
  setTimeout(() => moves.forEach(([el]) => el.style.transition = ""), MS + 50);
}).observe(document.documentElement, { childList: true, subtree: true, characterData: true, attributes: true });

// ── List cells: select a rectangle of cells, as in a spreadsheet, and copy it ──
// Drag across cells to select the rectangle between the first and the last; Ctrl+C copies it as
// tab-separated text, one line per row. Rows are the work items (tr[data-id]); group and subtask rows are skipped.
// Row drags start from the grip only, so the two never compete for the same pointer. A selection can start on any cell, a
// dropdown or the title included: only a press that moves to another cell selects, and that press's click is swallowed, so
// a still click still works the cell's control.
const _cells = { anchor: null, focus: null, dragging: false, moved: false };   // { row, col, table }

function _cellAt(target) {
  const td = target?.closest?.("td"), tr = td?.closest("tr[data-id]");
  if (!tr || target.closest("input, a, select, textarea, .grip, .chev, .pop")) return null;
  const table = tr.closest("table");
  return { row: [...table.querySelectorAll("tr[data-id]")].indexOf(tr), col: td.cellIndex, table };
}

function _paintCells() {
  for (const td of document.querySelectorAll("td.cs")) td.classList.remove("cs");
  const a = _cells.anchor, f = _cells.focus;
  if (!a || !f) return;
  const rows = [...a.table.querySelectorAll("tr[data-id]")];
  for (let r = Math.min(a.row, f.row); r <= Math.max(a.row, f.row); r++)
    for (let c = Math.min(a.col, f.col); c <= Math.max(a.col, f.col); c++) rows[r]?.cells[c]?.classList.add("cs");
}

function _clearCells() { _cells.anchor = _cells.focus = null; _cells.dragging = false; _paintCells(); }

// List header: the whole header is Radzen's reorder handle (.rz-column-drag, stretched under the title by app.css), and that
// handle swallows clicks. A press that does not move is a sort, so it is handed to the header's own sort handler (its div).
let _hdDown = null, _hdDrag = false;
document.addEventListener("pointerdown", (e) => {
  _hdDown = e.target.closest?.(".tbl-wrap thead .rz-column-drag") ? { x: e.clientX, y: e.clientY } : null;
  const resizer = e.target.closest?.(".tbl-wrap .rz-column-resizer");   // a resize: a guide line down the table and the resize pointer until release
  if (resizer) { resizer.closest("th").dataset.resizing = ""; document.body.classList.add("is-resizing"); }
}, true);
document.addEventListener("pointerup", () => { document.body.classList.remove("is-resizing"); for (const th of document.querySelectorAll("th[data-resizing]")) delete th.dataset.resizing; }, true);
document.addEventListener("click", (e) => {
  const drag = e.target.closest?.(".tbl-wrap thead .rz-column-drag");
  if (drag && _hdDown && Math.hypot(e.clientX - _hdDown.x, e.clientY - _hdDown.y) <= 4) drag.parentElement.click();
}, true);

document.addEventListener("pointerdown", (e) => {
  if (e.button !== 0) return;
  const cell = _cellAt(e.target);
  if (!cell) { if (!e.target.closest?.(".tbl-wrap")) _clearCells(); return; }
  _cells.moved = false;
  if (e.shiftKey || e.ctrlKey || e.metaKey) return;   // the board's select click (BoardPage), as on a card
  _cells.anchor = _cells.focus = cell;
  _cells.dragging = true;
  if (!e.target.closest("button, .dd, .menu-wrap")) _paintCells();   // a press on a control paints nothing until it moves
});
document.addEventListener("pointermove", (e) => {
  if (_hdDown && !_hdDrag && Math.hypot(e.clientX - _hdDown.x, e.clientY - _hdDown.y) > 4) { _hdDrag = true; document.body.classList.add("is-dragging"); }
  if (!_cells.dragging) return;
  const cell = _cellAt(document.elementFromPoint(e.clientX, e.clientY));
  if (cell && cell.table === _cells.anchor.table && (cell.row !== _cells.focus.row || cell.col !== _cells.focus.col)) { _cells.focus = cell; _cells.moved = true; _paintCells(); }
});
document.addEventListener("pointerup", () => { _cells.dragging = false; if (_hdDrag) { _hdDrag = false; document.body.classList.remove("is-dragging"); } });
document.addEventListener("click", (e) => { if (_cells.moved) { _cells.moved = false; e.stopPropagation(); e.preventDefault(); } }, true);   // the press that selected is not a click
document.addEventListener("keydown", (e) => { if (e.key === "Escape" && _cells.anchor) _clearCells(); });

/** The selected cells as tab-separated text, one line per row. */
function _cellsText() {
  const a = _cells.anchor, f = _cells.focus, rows = [...a.table.querySelectorAll("tr[data-id]")], lines = [];
  for (let r = Math.min(a.row, f.row); r <= Math.max(a.row, f.row); r++) {
    const vals = [];
    for (let c = Math.min(a.col, f.col); c <= Math.max(a.col, f.col); c++)
      vals.push((rows[r]?.cells[c]?.innerText ?? "").trim().replace(/\s*\n\s*/g, ", "));   // stacked features become "a, b"
    lines.push(vals.join("\t"));
  }
  return lines.join("\n");
}
// Ctrl+C is taken in the capture phase, before the grid's own key handling; the copy event covers an Edit > Copy menu.
addEventListener("keydown", (e) => {
  if (!e.isTrusted || !(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "c" || !_cells.anchor || _typing(e.target)) return;
  e.preventDefault(); e.stopPropagation();
  navigator.clipboard?.writeText(_cellsText());
}, true);
document.addEventListener("copy", (e) => {
  if (!_cells.anchor || !_cells.focus || _typing(e.target)) return;
  e.clipboardData.setData("text/plain", _cellsText());
  e.preventDefault();
});
