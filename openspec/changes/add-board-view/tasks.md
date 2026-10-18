# Tasks

## 1. Board rendering

- [x] 1.1 Build the toolbar (Board/List, group, sort incl. feature, contributor filter, show work items/subtasks, compact cards) and per-repo persistence; verify with bUnit tests that each option re-renders the expected columns and counts from the fixture repo
- [x] 1.2 Build the card, subtask card, column and feature-group switcher from the mockup; verify board, compact and subtask modes render the fixture in a bUnit snapshot
- [x] 1.3 Add custom sub-categories (stored in x-rtm.lanes) and the column header menu; verify the "Add a sub-category" and "Drop into lane" scenarios
- [ ] 1.4 Build the list table with header sorting, collapsible groups, expandable rows, inline status/priority and "+ Add item"; verify the "Sort by header" and "Expand one row" scenarios

## 2. Moving work

- [ ] 2.1 Implement drag and drop for status, feature and contributor columns through the workspace write-back; verify the "Change status", "Drop into lane" and "Locked feature" scenarios in tests
- [ ] 2.2 Implement archive on drop to Completed through the `openspec archive` CLI with a confirmation dialog; verify against a temp copy of the fixture that the folder moves and a CLI failure restores the card

## 3. Creating and merging

- [ ] 3.1 Implement "Add work item here" and "+ sub-group"; verify the new change and capability files pass `openspec validate --all`
- [x] 3.2 Implement multi-select and merge with requirement-conflict detection; verify the "Merge two" and "Requirement conflict" scenarios against the fixture
