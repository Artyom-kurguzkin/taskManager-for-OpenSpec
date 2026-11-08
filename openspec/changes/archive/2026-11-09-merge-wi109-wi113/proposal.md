# Proposal

look at the last changelog

instead of add subgroup for collumns in the board view, i want the swimlanes feature from github projects

* it should also work for the list view and calendar view

## Why

Per-column subgroups had to be named and maintained column by column. GitHub Projects' swimlanes group every column by one field at once, and a drag moves a card across columns and rows together.

## What Changes

- A "swimlanes" view option: none, priority or label. On the board each value is a row across every column; dropping a card into a row's column sets its status and that field. The list groups rows by the swimlane value above the column; the roadmap groups by it too.
- Custom subgroups ("+ Add subgroup", "Remove subgroup") and the automatic priority subgroups are gone from the board. The lane storage in the WorkItems module is no longer used by the board.
