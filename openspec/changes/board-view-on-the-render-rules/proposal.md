# Proposal

## Why

Every board render redraws every WorkItemCard (110-460 per click) because cards take reference-type parameters and four new lambdas per render, and plain clicks on project names, aligned column headers and swimlane headers redraw the whole board even when nothing changed. WorkItemCard also resolves the clock and reads test results per render.

## What Changes

- WorkItemCard skips redraws whose inputs did not change (RenderKey) and gets cached per-card callbacks; selection and examine marks reach only the cards that changed.
- Project, column and swimlane header clicks render only when they changed a selection.
- No per-render service lookups or test-result reads in cards.
