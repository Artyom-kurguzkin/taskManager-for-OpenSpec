# Tasks

## 1. Show what an agent added or changed

- [x] 1.1 Model: `AgentBy/AgentAt/ReviewedAt` on work items and notes, `Subtask.Agent`; unreviewed = stamped and (never reviewed or reviewed before the stamp)
- [x] 1.2 `MarkReviewed` for items, notes and subtasks; the stamp is always later than the agent's
- [x] 1.3 `AgentChip` on cards, list rows, subtasks, work item page and note page
- [x] 1.4 "Agent changes only" filter in the side menu
- [x] 1.5 Verify in the real app, light and dark
- [x] 1.6 Repo CLAUDE.md: stamp agent work

## 2. Agent conventions file for every project

- [x] 2.1 `AgentGuide.Ensure(repoRoot)` with a version marker; create / refresh / leave alone
- [x] 2.2 Called when a project opens; the notice is shown once by the layout
- [x] 2.3 Verify in the real app
