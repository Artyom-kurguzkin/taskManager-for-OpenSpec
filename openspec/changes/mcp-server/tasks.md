# Tasks

## 1. Allocator (chunk A)

- [ ] 1.1 Exclusive lock and counter files under the git common dir, sibling worktrees scanned, `%TEMP%` fallback outside git <!-- agent: Claude -->
- [ ] 1.2 Work-item and note creation, merge, split and duplicate repair allocate through it; writes become temp + move <!-- agent: Claude -->

## 2. Revision (chunk B)

- [ ] 2.1 Work items carry a revision hash; updates and notes refuse a stale one with the current revision in the message <!-- agent: Claude -->

## 3. Tools (chunks C, E)

- [ ] 3.1 `TaskManager.Mcp` library with the seven `board_*` tools, structured content, agent stamping, repo resolution, server instructions <!-- agent: Claude -->
- [ ] 3.2 Wire the allocator and revision in; test names checked against the latest trx; validate after writes <!-- agent: Claude -->

## 4. Hosts (chunks D, F)

- [ ] 4.1 `--serve`: Kestrel on 127.0.0.1, bearer token, Origin check, server.json, /health, /shutdown, idle exit, file logging <!-- agent: Claude -->
- [ ] 4.2 `--mcp` stdio shim: discover, start or replace the server, forward; in-process fallback <!-- agent: Claude -->
- [ ] 4.3 Desktop app hosts the endpoint when the port is free; Settings shows server status and the `.mcp.json` snippet <!-- agent: Claude -->

## 5. Guide and docs (chunk G)

- [ ] 5.1 REPO-TASKS.md v12 "use the tools when present", CLAUDE.md, README, `.mcp.json` in this repo <!-- agent: Claude -->
