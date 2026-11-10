# Proposal

## Why

The worklog shows status changes, links and subtask ticks, but not everything that happens to an item: a changed title, priority, label, dates, feature, contributors or plan, an edited description, a pasted attachment, or the subtasks board_create writes leave no trace. Decisions are buried in ordinary notes. Agents write one summary at the end instead of entries as they go, and the guide still presents editing openspec/ by hand as a normal path. The user asked: "From now on, use the mcp to interact with the app. Subagents and supervisor should incrementally add log entries for tasks in the works as they go. DECISIONS should be emphasised: clear and brief. Whenever attachment or subtask or whatever is added -> event in the logs. I should be able to easily hide such if needed when reading log entries."

## What Changes

- Every write to a work item or its notes records an event: title, priority, label, dates, feature, contributor added/removed, plan linked/unlinked, description edited, attachment added, subtasks written by board_create (one event each). Same wire format in `events.log`.
- Notes get a `kind`: `decision`. A decision is emphasised in the worklog (DECISION label, accent mark, no frame) and is written clear and brief: what was decided and why, two lines.
- The Worklog block's filters gain **Decisions** and **Edits** (field changes and attachments), so housekeeping events can be hidden while reading, and decisions can be read alone.
- `board_note` takes `kind: decision`; `board_list` no longer breaks its own output schema when there is no next page.
- Agent guide v15 and the server instructions: the repo-tasks tools are the only way to touch the board when they are present; supervisor and subagents write short log entries as the work goes (one per event: chunk started, test run, blocker, result), decisions as decision notes; the hand rules remain only for when the tools are absent.
