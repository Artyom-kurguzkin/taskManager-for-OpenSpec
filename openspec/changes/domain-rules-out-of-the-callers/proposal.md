# Proposal

## Why

By the reference's 3am rule, anything a caller with nobody at the window needs belongs in a module. Today about 20 domain rules sit inside the MCP tools (BoardTools.cs), and the host duplicates others: duplicate-id repair twice, the worklog feed twice (Worklog.razor and BoardTools), status history from events in AnalyticsModel, "changed in period" in BoardModel and sync-conflict notes in BoardSyncService. So the UI and agents can disagree.

## What Changes

- The rules move into their modules (WorkItems, Notes, Features, LinkedTestRuns, OpenSpecStore); the MCP tools and the host only call module Apis and shape the result.
- One definition per rule: duplicate repair, the worklog feed, status history.
