# Proposal

## Why

Agents edit `openspec/` by hand following a 200-line guide: next id from the max over every yaml, agent stamps, note numbers, test names matching the trx, the pinned validator. Every convention is one an agent can get wrong, and the duplicate-id repairs in the history show they do. The app already holds the correct implementation in its module facades; it just is not reachable from an agent.

## What Changes

- A tool library over the module facades exposes the board as seven MCP tools (`board_list`, `board_get`, `board_features`, `board_create`, `board_update`, `board_note`, `board_validate`), stamping every write as the agent's and validating with the pinned CLI.
- Work-item and note ids are allocated under a cross-process lock in the git common dir, scanning every worktree, so two agents or the app and an agent never hand out the same number.
- Edits carry a revision and are refused when the item changed since it was read.
- The app gains two headless modes: `--serve`, one stateless Streamable HTTP server on 127.0.0.1 for every agent and every repository, and `--mcp`, a stdio shim that finds or starts that server and forwards to it (or serves in-process when it cannot), telling it which repository and agent it speaks for. Settings shows whether the server is running and starts or stops it.
- `openspec/REPO-TASKS.md`, `CLAUDE.md` and a `.mcp.json` tell agents to use the tools when present.

Plan: `DevResources/plans/mcp-server-2026-11-09.md`.
