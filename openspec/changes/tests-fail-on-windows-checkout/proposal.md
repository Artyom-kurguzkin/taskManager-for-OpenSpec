## Why

On this machine 18 tests of the fast suite fail on an untouched HEAD (e050d77), so a red run no longer says whether a change broke something. Found on 6 Nov while integrating #wi97: the same 18 fail before and after that work.

## What Changes

- Find the cause and make the fast suite green on a fresh Windows checkout. The fixture files under `TaskManager.Tests/Fixtures/sample-repo` are checked out with CRLF line endings (`core.autocrlf=true`, no `.gitattributes`), and several of the failing tests compare file text against `\n`; that explains some of them for certain. The agent-stamp and agent-chip tests among the 18 are not yet explained.

## The failing tests

- A note an agent stamped is flagged until reviewed; reviewing adds reviewedAt and leaves the text alone
- A subtask line ending in an agent comment is flagged; ticking keeps the stamp, editing its text or marking it reviewed removes it
- Adding a feature from the board lists it in config.yaml instead of writing an empty (invalid) spec
- Adding a subgroup to a column saves it in config.yaml, and dropping an item into it is remembered
- Adding a subtask appends it to the last group with the next number
- Agent-added work carries no agent chip on its card or list row; the review click is on the item page, the filter still finds it
- An agent already listed among the contributors is not shown twice
- Changing the status saves it in x-rtm and keeps the rest of the file
- Commits and tests linked by hand are saved in x-rtm, each list on one line
- Creating a work item never changes the number of a change created outside the app
- Deleting a work item only hides it: the change folder stays and x-rtm records hidden
- In the list an agent subtask carries a chip whose click clears that subtask; the row itself has none
- On the work item page the agent is a contributor chip, lilac while unreviewed and the click that reviews it; the title row has no chip; subtasks and notes keep theirs
- Saving metadata keeps every other key and comment byte-for-byte
- Saving metadata to a change without x-rtm appends the block
- The first edit of a change created outside the app writes its provisional number
- The side menu can show only work with unreviewed agent changes
- Work an agent stamped is flagged until the user marks it reviewed, and flagged again when the agent changes it later
