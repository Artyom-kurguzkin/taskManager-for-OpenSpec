# Tasks

## 1. One MCP server for every agent · Allocator (chunk A)

- [x] 1.1 Exclusive lock and counter files under the git common dir, sibling worktrees scanned, `%TEMP%` fallback outside git
- [x] 1.2 Work-item and note creation, merge, split and duplicate repair allocate through it; writes become temp + move

## 2. One MCP server for every agent · Revision (chunk B)

- [x] 2.1 Work items carry a revision hash; updates and notes refuse a stale one with the current revision in the message

## 3. One MCP server for every agent · Tools (chunks C, E)

- [x] 3.1 `TaskManager.Mcp` library with the seven `board_*` tools, structured content, agent stamping, repo resolution, server instructions
- [x] 3.2 Wire the allocator and revision in; test names checked against the latest trx; validate after writes; every call re-reads the repository; the shim tells the server its repository and agent name <!-- agent: Claude -->

## 4. One MCP server for every agent · Hosts (chunks D, F)

- [x] 4.1 `--serve`: Kestrel on 127.0.0.1, bearer token, Origin check, server.json, /health, /shutdown, idle exit, file logging
- [x] 4.2 `--mcp` stdio shim: discover, start or replace the server, forward; in-process fallback
- [x] 4.3 Settings shows the server's status with a Start/Stop button and the `.mcp.json` snippet; the desktop app does not host the endpoint itself (the modules keep one current repository per process) <!-- agent: Claude -->

## 5. One MCP server for every agent · Guide and docs (chunk G)

- [x] 5.1 REPO-TASKS.md v12 opens with how to detect, turn on (`.mcp.json` entry, `claude mcp add`, `--serve`, token path) and use the tools, hand-editing as fallback
- [x] 5.2 CLAUDE.md, README and `.mcp.json` in this repo point at the shim
