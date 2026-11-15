# Tasks

## 1. agents-1

- [x] 1.1 Settle window of 10 s with a 5 min deferral cap; ignore list for temp and editor files <!-- agent: Claude -->
- [x] 1.2 Postponement cap: never past 15 min after the first unsynced change <!-- agent: Claude -->

## 2. agents-2

- [x] 2.1 Per-change-folder store lock across check, temp write and rename; unique temp names <!-- agent: Claude -->
- [x] 2.2 events.log appended under the lock instead of refused when stale <!-- agent: Claude -->

## 3. agents-3

- [x] 3.1 PreToolUse hook denying direct edits under openspec/changes while board tools are loaded <!-- agent: Claude -->
- [x] 3.2 Card badge from agentBy/agentAt; hidden-desktop shots light and dark <!-- agent: Claude -->
