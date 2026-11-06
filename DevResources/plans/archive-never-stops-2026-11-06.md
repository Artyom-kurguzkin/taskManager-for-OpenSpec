# Archive never stops

Status: done, in review (6 Nov 2026). Fast suite: the same 18 failures as an untouched HEAD, none new (#wi98). 14 real-CLI tests pass. Hidden desktop checked in light and dark. Work item: `openspec/changes/archive-never-stops`.

## Why

Moving a card to Completed runs `openspec archive <change> --yes`. When the CLI refuses, the app shows its raw output
and the card stays put. The user hit this in another repository:

```
module-boundaries: target spec does not exist; only ADDED requirements are allowed for new specs.
MODIFIED and RENAMED operations require an existing spec.
Aborted. No files were changed.
```

The user's ask: the app handles these cases itself; they never see such an error again.

The app already repairs some stale deltas before the CLI runs (`DeltaSpec.Refresh`, #wi96), but `RefreshDeltas` skips
a capability whose main spec does not exist, and several other refusals are not covered.

## Research: every way `openspec archive` refuses

Source: the pinned CLI itself, `@fission-ai/openspec@1.14.3`, `dist/core/archive.js`, `dist/core/specs-apply.js`
(`buildUpdatedSpec`), `dist/core/validation/validator.js` (`validateChangeDeltaSpecs`), `dist/core/parsers/requirement-blocks.js`.
The package ships no docs folder; the README and the agent guide (`openspec/REPO-TASKS.md` section 3) describe the delta format.

Order the CLI applies a delta in: RENAMED, REMOVED, MODIFIED, ADDED. Names compare exactly after trimming; a "near
miss" is a name equal after lower-casing and collapsing whitespace.

| # | Refusal | Cause | App's answer |
|---|---------|-------|--------------|
| 1 | target spec does not exist; only ADDED allowed | MODIFIED / RENAMED against a capability with no main spec | repair: MODIFIED becomes ADDED, RENAMED dropped |
| 2 | MODIFIED failed - not found | requirement not in the main spec | repair: near miss takes the main spec's exact name; otherwise becomes ADDED |
| 3 | MODIFIED failed - current spec contains scenario(s) not present | stale block | repair (already there): scenarios copied back |
| 4 | ADDED failed - already exists | another change added it first | repair (already there): becomes MODIFIED |
| 5 | ADDED failed - differs only in case or spacing | near miss | repair: takes the exact name, becomes MODIFIED |
| 6 | REMOVED failed - not found, but "X" exists | near miss | repair: takes the exact name |
| 7 | RENAMED failed - source not found | neither name in the main spec | repair (already there): pair dropped |
| 8 | RENAMED failed - source not found, but "X" exists | near miss on FROM | repair: takes the exact name |
| 9 | RENAMED failed - target already exists (or near miss) | both names exist | repair: pair dropped |
| 10 | RENAMED entry has no matching FROM / TO | unpaired line | repair: line dropped |
| 11 | duplicate requirement in ADDED / MODIFIED | listed twice | repair (already there): merged |
| 12 | duplicate in REMOVED, duplicate FROM / TO | listed twice | repair: first kept |
| 13 | present in multiple sections (MODIFIED and ADDED) | | repair (already there): merged |
| 14 | present in multiple sections (… and REMOVED), RENAMED and REMOVED | contradiction | repair: the REMOVED entry goes (keeping text is the safe side) |
| 15 | when a rename exists, MODIFIED must reference the NEW header | MODIFIED names FROM | repair: block takes the TO name |
| 16 | No delta sections found / requirement outside a delta section | requirements with no `## X Requirements` heading | repair: they go under ADDED |
| 17 | section heading in another case (`## Added requirements`) | CLI reads it, the app did not | repair: read case-insensitively |
| 18 | requirement lacks SHALL/MUST, lacks a scenario; rebuilt spec fails validation | content | fallback: `--no-validate` |
| 19 | target spec is structurally invalid; removes the last requirement (needs `retire_capabilities`); delta at `specs/spec.md`; no operations parsed | cannot be merged mechanically | fallback: `--skip-specs`, with a note on the item |
| 20 | archive folder for today exists | same name archived today | (already there) folder renamed first |
| 21 | incomplete tasks | | (already there) `--yes` |
| 22 | Node.js / npx missing, timeout | environment | the only errors the user still sees |

## Decisions

1. **Repair first** (table rows 1 to 17), in `DeltaSpec.Refresh`, before the CLI sees the change. Deterministic, idempotent,
   byte-identical when nothing is wrong. A missing main spec is an empty main spec.
2. **Then a ladder** for whatever is left: `archive --yes`, then `--yes --no-validate`, then `--yes --no-validate --skip-specs`.
   The item always completes. No retry when the CLI could not be started or timed out.
3. **The last rung is recorded**, not shown: the archived item gets a note "Archived without merging its specs" with the
   CLI's reason, so the spec text is not silently lost (it stays in the archived change's `specs/`).
4. No own merge engine: the CLI stays the only thing that writes main specs.

## Chunks

- **A. Repairs** (`WorkItems/BusinessRules/DeltaSpec.cs`, new test file). Unit tests on `Refresh`; black-box: the real CLI
  archives the refreshed delta.
- **B. Ladder** (`WorkItems/Commands.cs`, `Infrastructure/OpenSpecCli.cs`, `WorkItemsModule.cs`, `WorkItemsApi.cs`, new test file).
  Unit tests with a fake archiver; `RefreshDeltas` treats a missing main spec as empty.
- **C. Integration** (main session): the user's exact case end to end through the real CLI; hidden-desktop check in light and dark; board.

## Verification

- Fast suite green; `Category=OpenSpecCli` tests green.
- Hidden desktop: a demo repo with a MODIFIED delta against a capability that has no main spec; drag the card to
  Completed; no error popup; the spec is created; screenshots in light and dark.
