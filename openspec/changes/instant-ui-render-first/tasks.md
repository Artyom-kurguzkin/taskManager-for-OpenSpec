# Tasks

## 1. Chunk 1: render first in the board

- [ ] 1.1 Set skips equal options, re-sorts in memory, renders, saves settings debounced on the thread pool <!-- agent: Claude -->
- [ ] 1.2 Land projects the moved cards before MoveInto writes; one Load, one version bump; mismatch reloads <!-- agent: Claude -->
- [ ] 1.3 Load bumps loadVersion/listVersion only when the projection changed; render-budget tests for ArrowUp and drop <!-- agent: Claude -->

## 2. Chunk 2: the write path off the UI thread

- [ ] 2.1 RepoPage.Run applies the projection, renders, yields, then Workspace.Do on the thread pool; rollback and toast on failure <!-- agent: Claude -->
- [ ] 2.2 Notify fires Changed first, runs the duplicate scan and PlansState afterwards under a lock; OnChanged skips the page's own echo <!-- agent: Claude -->
- [ ] 2.3 Per-path write semaphore in OpenSpecStore; AsyncLocal repo scope captured in background work; WritingWithoutBlockingTests <!-- agent: Claude -->

## 3. Chunk 3: git status and config reads

- [ ] 3.1 GitCache.Uncommitted returns the last known set and refreshes on the thread pool <!-- agent: Claude -->
- [ ] 3.2 config.yaml reads cached behind the store version; GetWarnings leaves the board markup; FeaturesCache keyed on the specs stamp <!-- agent: Claude -->

## 4. Chunk 4: the verification routine

- [ ] 4.1 New-DemoRepo -Used preset: list grouping, header sort, stale Order, mixed-priority group with ties <!-- agent: Claude -->
- [ ] 4.2 hidden-app.ps1 helpers ListRows, Select, Press, Assert-Swap, Set-ListGrouping, Set-GridSort; tests/exploratory.ps1 checklist <!-- agent: Claude -->
