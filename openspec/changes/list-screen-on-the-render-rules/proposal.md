# Proposal

## Why

Switching board to list takes 420-501 ms (302 list pills built, twice, by Radzen's two passes), and every examine or Shift-click still re-renders every grid row. The plan's general rule: a RenderBoundary keyed on a data version around the grid, selection and examine marks applied outside it, and BoardPage in strict render mode.

## What Changes

- RenderBoundary (shared component) renders its content only when its version changes; it wraps the list grid.
- RowMarks applies selected/examined/waits marks as one style rule per marked row outside the grid; app.js selectors follow.
- BoardPage list path in strict mode on the base component; render-budget tests.
