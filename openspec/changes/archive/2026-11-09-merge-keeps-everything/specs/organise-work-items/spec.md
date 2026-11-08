## MODIFIED Requirements

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘/Shift-click) and merge them. Merge SHALL ask which selected item is the main one ("Merge into…"): the merged work item keeps that item's title and description, and every other source's description becomes a note "Merged from #wi<N> <title>" on it. It SHALL combine the tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. No information SHALL be lost: it SHALL keep the sources' notes and other files, the label, linked commits and tests, lanes and dependencies, the task groups under headings naming their source, and the numbers they came from. Items that waited on a source SHALL wait on the merged item. Merging SHALL be refused when two active sources change the same requirement. Archived items SHALL always be mergeable, since their specs are already merged into `openspec/specs`: with only archived sources the result is archived and keeps all their specs; with an active source the result is active and keeps the archived specs under `from-wi<id>/specs/`, not in its own `specs/`, so archiving it cannot apply them twice.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105, picks Merge and chooses #wi105 as the main one
- **THEN** one new change has #wi105's title and description, a note "Merged from #wi107" with #wi107's description, all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

#### Scenario: Requirement conflict
- **WHEN** both sources have a delta for requirement "Session timeout" in the same capability
- **THEN** nothing changes and the message names the requirement and both work items

#### Scenario: Notes and links survive
- **WHEN** #wi107 has a note, a linked commit and a linked test and is merged with #wi123
- **THEN** the merged item shows that note in its change log, lists that commit and test, and lists commits that mention #wi107

#### Scenario: Archived items that changed the same requirement
- **WHEN** two completed items both changed requirement "Keyboard hints" and the user merges them
- **THEN** the merge succeeds, the result is archived, and both texts of the requirement are kept in its spec

#### Scenario: Archived item merged into an active one
- **WHEN** a completed item that changed "Session timeout" is merged with an active item
- **THEN** the result is active, its own specs/ folder does not contain that change, and the text is kept in `from-wi<id>/specs/`

## ADDED Requirements

### Requirement: Merged content by source and date
A merge SHALL head each source's task group "#wi<N> <title> · merged <date>" and SHALL stamp every note it moves in with `mergedFrom` and `mergedAt`. The Subtasks tile SHALL show each task group as a collapsible header with its done and total counts (a fully done group starts collapsed; a single group shows no header). The Change log SHALL list the item's own notes first, then one collapsible section per merged source, newest merge first, headed "From #wi<N> · merged <date>".

#### Scenario: Three items merged
- **WHEN** #wi64 and #wi92 are merged into #wi85 on 9 Nov
- **THEN** #wi85's subtasks show the groups "#wi64 Notes editor · merged 2026-11-09" and "#wi92 Changelog UI issues · merged 2026-11-09", and its change log ends with "From #wi92 · merged 9 Nov" and "From #wi64 · merged 9 Nov"
