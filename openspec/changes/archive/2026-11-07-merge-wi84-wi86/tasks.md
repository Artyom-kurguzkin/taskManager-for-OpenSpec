# Tasks

## 1. Open several repositories as tabs + Every open project on one board · Open several repositories as tabs

- [x] 1.1 Remember open repos in settings (`x-rtm`-style list, not per-repo board state); verify round-trip
- [x] 1.2 `Workspace.OpenTab` / `CloseTab`, with the last tab staying open; verify open, switch and close scenarios
- [x] 1.3 Side nav tab switcher (list, switch, close, "Open another repository…"); verify from the rendered page

## 2. Open several repositories as tabs + Every open project on one board · Every open project on one board · Store

- [x] 2.1 OpenSpecStore: open repositories as a set (version and watcher each), `Use`, `Keep`, `OpenRoots`; a plain `Open` closes the others
- [x] 2.2 WorkItems, Features, Notes, LinkedTestRuns caches per repository; GitHistory state per repository
- [x] 2.3 `Workspace.In`, `Projects`, every tab opened in the store at start

## 3. Open several repositories as tabs + Every open project on one board · Every open project on one board · Board and list

- [x] 3.1 A block per project with a heading (chevron collapses, menu hides); one shared header row when the columns align
- [x] 3.2 Selection, drag, arrow keys, menus and commands carry the project; a cross-project drop is refused
- [x] 3.3 List grouped by project, then column
- [x] 3.4 Hidden projects in the side menu's Hidden list
- [x] 3.5 Verify in the real app with two repositories as tabs
- [x] 3.6 Reorder projects: Shift-click the heading, arrow keys move it (tab order, remembered)
- [x] 3.7 "Remove a project…" at the end of the repository menu: pick one, its tab closes (pick-one dialog in UiService)
- [x] 3.8 New work item asks for the project when several are open (side menu, shared header row)
- [x] 3.9 Sticky column headers on the board

## 4. task tracking bugs · Numbers used twice resolve themselves

- [x] 4.1 `ResolveDuplicateIds` in WorkItems: keeper by archived, created, folder; the rest renumbered to the next free number
- [x] 4.2 Workspace runs it on every change; toast and change-log note on the renumbered change
- [x] 4.3 Verify in the real app with a copied change folder

## 5. task tracking bugs · Archiving refreshes stale delta specs itself

- [x] 5.1 `DeltaSpec.Refresh(delta, main)`: scenarios missing from a MODIFIED block are appended from the main spec; idempotent; keeps the file's line endings
- [x] 5.2 `SetStatus` → Completed runs it on every delta spec of the change before the CLI
- [x] 5.3 Proven against the real CLI (OpenSpecCli test category)
