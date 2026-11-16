# Tasks

## 1. 1. Research

- [x] 1.1 principles: state and render models with sources <!-- agent: Claude -->
- [x] 1.2 open-source projects: how they do it in code <!-- agent: Claude -->
- [x] 1.3 map of this app's state, events and render triggers <!-- agent: Claude -->
- [x] 1.4 measured cost per screen (board, side menu, pages) <!-- agent: Claude -->

## 2. 2. Plan

- [x] 2.1 plan file with the model, migration chunks and enforcement <!-- agent: Claude -->
- [x] 2.2 caching model: central invalidation by change signals, no IO in render paths <!-- agent: Claude -->
- [x] 2.3 inventory of duplicated low-level helpers and building blocks <!-- agent: Claude -->
- [ ] 2.4 subtask tick on the item page still 244-310 ms (baseline 130-244) with an occasional second redraw 1.5 s later: the tick reloads the whole page; patch the changed subtask in place <!-- agent: Claude -->
- [ ] 2.5 period filter back to 'any time' redraws every list row (12k mutations, ~1 s) <!-- agent: Claude -->
- [ ] 2.6 AgentGuide, GitAttributes, WorklogTracking from Helpers/ into OpenSpecStore; BoardSyncService rules into the BoardSync module (timer loop stays in BackgroundJobs/); BoardOptions into AppState/ <!-- agent: Claude -->
- [ ] 2.7 real file watcher off by default in tests: ~15 tests call Refresh() before Notify() <!-- agent: Claude -->
