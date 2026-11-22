# Proposal

## Why

Agents should log their work to the item's feed through the MCP tools, following an event standard (started, found, decision, tests red, tests green, verified, blocked, done), without it costing much context. Today board_note needs a title and a revision and returns a JSON object, board_log is capped at one line of 300 characters, and the eight tools' definitions carry about 5,000 tokens of prose into every agent's context before it does anything.

## What Changes

- board_log becomes the agent's one event tool: multi-line text (up to 4,000 characters; longer becomes a note), an optional `kind` from the fixed event standard, no revision, no title, the agent name from RTM_AGENT or `agent`; it answers with one short line.
- The worklog shows the kind as a label on the event (started, found, decision, tests red, tests green, verified, blocked, done), so a chunk's progress reads at a glance.
- The tools' descriptions are cut to one line each with one-clause parameter help; the shared guidance lives once in the server instructions. Target: half the definition size.
- The delegate-workflow briefs tell agents to post with board_log and the kinds.
