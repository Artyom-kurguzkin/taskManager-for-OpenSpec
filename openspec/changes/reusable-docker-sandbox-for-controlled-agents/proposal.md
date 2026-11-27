# Proposal

## Why

The owner wants completely controlled agents: an editing agent that sees only the opened project, cannot reach the outside world except to search, cannot push, and cannot touch chosen folders. For this project that means openspec/ is changed only through the board's MCP tools (#wi121 note #n136). Testing is done by another, unsandboxed agent.

## What Changes

- A reusable tool outside this repository (`5-mine/agent-sandbox`): a Docker image with Claude Code, bash, PowerShell 7 and the .NET SDK, started on any project folder
- Only the project is mounted; per-project hidden paths (here `openspec`) appear empty and unwritable
- Network deny by default through an allow-list proxy: Claude and web search only; the owner allows other hosts from outside
- No git push or commit from inside; `.git` read-only
- The Claude desktop app and the VS Code extension attach over SSH
- MCP over HTTP from a host-side config; this project's board at the app's endpoint with its token
- No change to this app (a tool beside it): no spec delta
