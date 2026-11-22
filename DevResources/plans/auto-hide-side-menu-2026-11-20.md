# Auto-hiding side menu, 20 Nov 2026

Status: planned 20 Nov 2026. One chunk (worktree chunk-ap, CDP port 9433).

## The ask
"Make it so in collapsed view, the menu auto-collapses completely (hides away) and appears when mouse hovers near it."

## Decisions
1. Collapsed means hidden: the icon rail (`nav.side` without `expanded`) slides fully off the left edge and the page takes the whole width. Expanded stays as it is (☰ still toggles it).
2. It comes back on approach: a thin invisible hot zone along the left edge (about 8 px, full height) reveals the rail as an overlay above the page (no reflow of the board); it hides again a short moment (about 400 ms) after the pointer leaves both the zone and the rail. Pure CSS where possible (`:hover` on a wrapper holding the zone and the rail, transition on `transform`), no JS timers unless CSS cannot give the delay (a `transition-delay` on the hide does).
3. It never hides while it is in use: a popup or dropdown opened from the rail, keyboard focus inside it (`:focus-within`), or a drag in progress keep it shown.
4. Same pattern the OS and editors use (Windows auto-hide taskbar, VS Code's hidden activity bar, macOS Dock auto-hide): reveal on edge proximity, overlay, delayed hide.

## Verification
bUnit/markup tests for the classes and the hot zone; CDP on the hidden desktop: collapsed, pointer away (rail off-screen, board starts at x=0), pointer at x=2 (rail visible over the board), pointer away again (hidden after the delay), focus inside (stays); light and dark screenshots.
