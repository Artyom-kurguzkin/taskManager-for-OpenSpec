# Tasks

## 1. Numbers used twice resolve themselves

- [x] 1.1 `ResolveDuplicateIds` in WorkItems: keeper by archived, created, folder; the rest renumbered to the next free number
- [x] 1.2 Workspace runs it on every change; toast and change-log note on the renumbered change
- [x] 1.3 Verify in the real app with a copied change folder

## 2. Archiving refreshes stale delta specs itself

- [x] 2.1 `DeltaSpec.Refresh(delta, main)`: scenarios missing from a MODIFIED block are appended from the main spec; idempotent; keeps the file's line endings
- [x] 2.2 `SetStatus` → Completed runs it on every delta spec of the change before the CLI
- [x] 2.3 Proven against the real CLI (OpenSpecCli test category)
