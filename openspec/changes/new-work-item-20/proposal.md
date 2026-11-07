# Proposal

collumn contents should not overflow to the next ones (screenshot in changelog entry)

## Why

In the list view a cell's content (title, label pill, features, contributors, progress count) spilled into the next column when it did not fit.

## What Changes

- Every list cell clips its content to its column: text ends in an ellipsis with the full text as a tooltip, pills and chips shrink, the progress bar shrinks before its count does.
- The Label column starts wide enough for "enhancement".
