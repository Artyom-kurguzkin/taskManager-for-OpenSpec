# Proposal

## Why

One click in the list re-renders the whole grid: about 5,600 component renders with 60 rows, 84 % of them the status/label/priority pill editors (each a full RadzenDropDown with all its items, even closed). The click bubbles through three handlers, each causing a full page render, and the grid re-renders twice per render because its parameters are always new. In the real app an examine click takes 100 to 210 ms and grows with the number of rows; list tests are slow for the same reason.

## What Changes

- A render counter on the board page, and render-count tests for an examine click, a Shift-click, a background click and a status change.
- Handlers that change nothing (the wrapper's background click, the row click) no longer render the page: one render per click instead of three.
- List pills render as plain markup; one shared picker opens on demand for the clicked cell.
- Later, if still needed: the grid in its own component that renders only when the data or options change, with selection and examine marks applied outside it; parameter hygiene; virtualization for long lists.
