# Proposal

<!-- merged from #wi44 Shift + hint letters selects the item -->
## Why

Keyboard hints (F) could only click an element. Selecting cards or subtasks, which is a Shift-click with the mouse, had no keyboard path, and subtask texts had no badges at all.

## What Changes

- With the badges showing, typing a badge's letters with Shift held Shift-clicks that element: a card or subtask is selected instead of opened or edited.
- After a Shift-selection the badges stay up, so several items can be picked in a row; F or Esc hides them.
- Subtask texts get badges: plain letters edit the subtask, Shift + letters selects it.

## Capabilities

### Modified Capabilities
- `app-shell`: Shift + hint letters selects; subtask texts are hinted.

## Impact

- `TaskManager/wwwroot/js/app.js`.

<!-- merged from #wi48 Keyboard hints open with Ctrl+Alt + Keyboard hints open with F -->
<!-- merged from #wi32 Keyboard hints open with Ctrl+Alt -->
## Why

Keyboard hints toggled on a bare Alt press, which fires by accident (Alt-Tab, Alt shortcuts in other apps, brushing the key).

## What Changes

- Ctrl+Alt (pressed in either order) toggles keyboard hints; Alt on its own no longer does.

## Capabilities

### Modified Capabilities
- `app-shell`: keyboard hints are toggled with Ctrl+Alt.

## Impact

- `TaskManager/wwwroot/js/app.js`.

<!-- merged from #wi43 Keyboard hints open with F -->
## Why

Keyboard hints opened with Ctrl+Alt, a two-key chord that is awkward to press and easy to forget. Vimium-style hint modes use a single key.

## What Changes

- F toggles keyboard hints, on and off, when the cursor is not in a text field (a text box, text area, dropdown or editable area). In a field, F types as usual.
- `f` is no longer a badge letter, so F while the badges are showing always hides them.
- Ctrl+Alt no longer toggles hints.

## Capabilities

### Modified Capabilities
- `app-shell`: keyboard hints are toggled with F.

## Impact

- `TaskManager/wwwroot/js/app.js`, a comment in `wwwroot/css/app.css`.
