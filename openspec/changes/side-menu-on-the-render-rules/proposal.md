# Proposal

## Why

The side menu redraws twice per click inside it (its own nav onclick bubbles), rebuilds its 7 Radzen dropdowns on every settings, workspace, layout or worklog event (even a theme change), renders folded sections invisibly, and tears down and rebuilds its option blocks when an item is examined or let go: 610 DOM changes on let-go. Its first expand after start takes 222-348 ms.

## What Changes

- The nav click handler does not render; a click in the menu redraws it once.
- The option blocks stay alive (hidden) while an item is examined; folded section bodies render only when open.
- The menu skips redraws for events that change nothing it shows.
