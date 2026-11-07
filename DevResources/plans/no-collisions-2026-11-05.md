# No collisions, ever — 5 Nov 2026

- **Status:** done
- **Date:** 2026-11-05
- **Author:** Claude (agent)
- **Work item:** #wi97 (and #wi87, which asked for it)

## Why

Archiving #wi88 failed with "Archive '2026-11-05-new-work-item-13' already exists": the app had reused a folder name that an archived change already held. The user's rule: the app must resolve every collision itself, never surface one.

## Every collision the app can meet

| Namespace | Where it collides | Resolution |
|-----------|-------------------|------------|
| Change folder name | create / merge / split picks a name that exists under `changes/` or `changes/archive/<date>-…` | pick the next free name across both |
| Archive name | `archive/<today>-<folder>` exists (same name archived twice in a day) | rename the folder (`-2`, `-3`) before the CLI runs |
| Work item id | two changes with the same `x-rtm.id` (agents, merges, archived copies) | existing resolver: the newer gets the next free id; audited against archive and concurrent writers |
| Note id | two `notes/n<id>.md` with the same id anywhere, or file name ≠ frontmatter `id` | renumber the newer; frontmatter follows the file name |
| Subtask number | duplicate `1.1` lines in `tasks.md` (agents appending) | renumber on write, tolerate on read |
| Lane name | adding a subgroup that exists in the column | no duplicate; adding is a no-op |
| Delta spec, ADDED | the requirement already exists in the main spec (another change archived first) | becomes MODIFIED with every main-spec scenario kept |
| Delta spec, duplicate | the same requirement twice in one delta (concatenated merges) | blocks merged into one |
| Delta spec, REMOVED/RENAMED | the target no longer exists | the block is dropped |
| Delta spec, MODIFIED stale | main spec gained scenarios | already handled: scenarios put back |
| Feature | create/rename to an existing name | already refused with a clear message (a choice, not a collision) |
| Attachment file | same file name in a note folder | already suffixed `-2`, `-3` |

## Chunks

| # | Chunk | Owner | Test |
|---|-------|-------|------|
| A | Folder names and same-day archive clash | sonnet, worktree | `EditingWorkItemsTests`, real CLI |
| B | Delta spec collisions resolved before archive | sonnet, worktree | `EditingWorkItemsTests`, real CLI |
| C | Note ids, subtask numbers, lane names; work-item id resolver audit | sonnet, worktree | `KeepingNotesTests`, `EditingWorkItemsTests`, `ReadingWorkItemsTests` |
| D | Integration, hidden-desktop archive-twice check, board | main session | full suite |

## Verification

Each chunk red first then green; the real CLI archives twice in one day with the same name, archives an ADDED-that-exists delta, and validates `--all --strict` afterwards; suite green; nothing committed.
