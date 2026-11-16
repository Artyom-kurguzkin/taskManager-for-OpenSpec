# Tasks

## 1. 1. List

- [x] 1.1 RenderBoundary around the grid, keyed on a data version <!-- agent: Claude -->
- [x] 1.2 RowMarks outside the grid; app.js follows <!-- agent: Claude -->
- [x] 1.3 strict mode and render-budget tests; board to list under 150 ms <!-- agent: Claude -->
- [ ] 1.4 bug: 'Examining a row in the list renders no grid rows' fails only in the full run (48 row renders = one late grid redraw after the before-count); suspects OnAfterRenderAsync FillGroups/Reload branches (BoardPage.razor.cs:481-499) and a late watcher-driven Load; make those branches idempotent or settle on RowRenders <!-- agent: Claude -->
