## MODIFIED Requirements

### Requirement: Merge work items
The user SHALL be able to select several cards (Ctrl/⌘/Shift-click) and merge them. The merged work item SHALL combine the proposals, tasks, delta specs, features and contributors of the sources, take the highest priority, and replace the sources. No information SHALL be lost: it SHALL keep the sources' notes and other files, the label, linked commits and tests, lanes and dependencies, the task groups under headings naming their source, and the numbers they came from. Items that waited on a source SHALL wait on the merged item. Merging SHALL be refused when two active sources change the same requirement. Archived items SHALL always be mergeable, since their specs are already merged into `openspec/specs`: with only archived sources the result is archived and keeps all their specs; with an active source the result is active and keeps the archived specs under `from-wi<id>/specs/`, not in its own `specs/`, so archiving it cannot apply them twice.

#### Scenario: Merge two
- **WHEN** the user selects #wi107 and #wi105 and picks "Merge into one work item"
- **THEN** one new change holds both proposals' text and all their subtasks, the two source folders are removed, and its change log names #wi107 and #wi105

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
