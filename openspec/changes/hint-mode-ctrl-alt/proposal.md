# Proposal

## Why

Keyboard hints toggled on a bare Alt press, which fires by accident (Alt-Tab, Alt shortcuts in other apps, brushing the key).

## What Changes

- Ctrl+Alt (pressed in either order) toggles keyboard hints; Alt on its own no longer does.

## Capabilities

### Modified Capabilities
- `app-shell`: keyboard hints are toggled with Ctrl+Alt.

## Impact

- `TaskManager/wwwroot/js/app.js`.
