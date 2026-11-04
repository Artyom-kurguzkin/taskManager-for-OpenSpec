## Why

The user set a standing workflow for sizeable tasks: research, a written plan, testable chunks delegated to cheaper models in separate worktrees, verification and merge, OpenSpec kept current, the result left uncommitted. The app had nowhere to keep plans the user could move, and no way to reach a plan from a work item.

## What Changes

- A **plans folder** per repository, a per-user setting (Settings → Plans folder, default `DevResources/plans`), so one person can keep plans under a private folder. Agents read it from `openspec/REPO-TASKS.md`: the guide carries the folder and is rewritten in every open project the moment the setting changes.
- `x-rtm.plan`: a repo-relative path to the item's plan. The work item page has a plan field and a chip that opens the plan **inside the app**.
- `/doc/<path>` renders any markdown file under the repository; paths outside it and missing files are "not found".
- Guide v4 opens with the workflow (research → plan → split → delegate in worktrees → verify and merge → update the board → leave uncommitted), so other agents follow it.

## Capabilities

### Modified Capabilities

- `workspace`: plans folder setting, guide contents.
- `work-item-details`: plan link, doc page.
