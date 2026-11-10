# Tasks

## 1. #wi96 No collisions, ever · Names · merged 2026-11-10

- [x] 1.1 Folder names free across changes/ and archive/; same-day archive clash renamed away (real CLI test)

## 2. #wi96 No collisions, ever · Specs · merged 2026-11-10

- [x] 2.1 ADDED-that-exists → MODIFIED, duplicate requirement blocks merged, REMOVED of nothing dropped (real CLI test)

## 3. #wi96 No collisions, ever · Ids · merged 2026-11-10

- [x] 3.1 Note ids counted across the archive; duplicates and file/frontmatter mismatches resolve on read
- [x] 3.2 Duplicate subtask numbers tolerated and renumbered; duplicate subgroup names a no-op

## 4. #wi96 No collisions, ever · Verify · merged 2026-11-10

- [x] 4.1 Hidden desktop: archive the same folder name twice in one day from the item page

## 5. Merge & Split Items bugs · Repairs

- [x] 5.1 Delta repairs for every fixable CLI refusal, a missing main spec included (unit tests and real CLI tests)

## 6. Merge & Split Items bugs · Ladder

- [x] 6.1 Archive retries without validation, then without merging specs; a note records the last rung
- [x] 6.2 The CLI's output is read as UTF-8, so its warning signs are not garbled in the note

## 7. Merge & Split Items bugs · Verify

- [x] 7.1 The reported case end to end through the real CLI
- [x] 7.2 Hidden desktop, light and dark: complete an item whose delta modifies a feature with no spec

## 8. #wi105 task tracking bugs · Open several repositories as tabs + Every open project on one board · Open several repositories as tabs · merged 2026-11-10

- [x] 8.1 Remember open repos in settings (`x-rtm`-style list, not per-repo board state); verify round-trip
- [x] 8.2 `Workspace.OpenTab` / `CloseTab`, with the last tab staying open; verify open, switch and close scenarios
- [x] 8.3 Side nav tab switcher (list, switch, close, "Open another repository…"); verify from the rendered page

## 9. #wi105 task tracking bugs · Open several repositories as tabs + Every open project on one board · Every open project on one board · Store · merged 2026-11-10

- [x] 9.1 OpenSpecStore: open repositories as a set (version and watcher each), `Use`, `Keep`, `OpenRoots`; a plain `Open` closes the others
- [x] 9.2 WorkItems, Features, Notes, LinkedTestRuns caches per repository; GitHistory state per repository
- [x] 9.3 `Workspace.In`, `Projects`, every tab opened in the store at start

## 10. #wi105 task tracking bugs · Open several repositories as tabs + Every open project on one board · Every open project on one board · Board and list · merged 2026-11-10

- [x] 10.1 A block per project with a heading (chevron collapses, menu hides); one shared header row when the columns align
- [x] 10.2 Selection, drag, arrow keys, menus and commands carry the project; a cross-project drop is refused
- [x] 10.3 List grouped by project, then column
- [x] 10.4 Hidden projects in the side menu's Hidden list
- [x] 10.5 Verify in the real app with two repositories as tabs
- [x] 10.6 Reorder projects: Shift-click the heading, arrow keys move it (tab order, remembered)
- [x] 10.7 "Remove a project…" at the end of the repository menu: pick one, its tab closes (pick-one dialog in UiService)
- [x] 10.8 New work item asks for the project when several are open (side menu, shared header row)
- [x] 10.9 Sticky column headers on the board

## 11. #wi105 task tracking bugs · task tracking bugs · Numbers used twice resolve themselves · merged 2026-11-10

- [x] 11.1 `ResolveDuplicateIds` in WorkItems: keeper by archived, created, folder; the rest renumbered to the next free number
- [x] 11.2 Workspace runs it on every change; toast and change-log note on the renumbered change
- [x] 11.3 Verify in the real app with a copied change folder

## 12. #wi105 task tracking bugs · task tracking bugs · Archiving refreshes stale delta specs itself · merged 2026-11-10

- [x] 12.1 `DeltaSpec.Refresh(delta, main)`: scenarios missing from a MODIFIED block are appended from the main spec; idempotent; keeps the file's line endings
- [x] 12.2 `SetStatus` → Completed runs it on every delta spec of the change before the CLI
- [x] 12.3 Proven against the real CLI (OpenSpecCli test category)
