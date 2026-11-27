# Proposal

## Why

Owner, 2026-11-27: "currently, one mouse click in the browser and shift-click are completely separate. instead, one click also selects one object. if i click on another one without shift, it de-selects the previous one and selects a new one. if i was using shift+click, then now i have two objects selected. applicable to all views (calendar, board, list)". Then: "double clicking on an item then opens its details view"; "clicking on already selected item" (opens it). Today a plain click opens or examines the item and leaves the selection alone; only Shift / Ctrl / ⌘-click touches it.

## What Changes

- A plain click on a card (board, calendar) or a row (list) selects that item alone: the previous selection is let go. With the side menu expanded it also examines it, as today.
- A plain click on an item that is already selected opens its details; a double-click opens its details.
- Shift / Ctrl / ⌘-click adds the item to the selection or removes it, as today.
- A click on empty space lets go of the selection, as today. Project headings, swimlane and column headers keep their current click meanings.
- UI-only: no unit tests; checked in the real app in the three views, light and dark.
