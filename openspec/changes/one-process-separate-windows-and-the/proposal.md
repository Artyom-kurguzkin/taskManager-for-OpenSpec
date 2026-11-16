# Proposal

## Why

The owner wants one process: "Separate window" today starts a second app process, and the MCP server runs as a separate --serve process. With the repository and author now per call (#wi181), both can live in the app process: windows share Workspace and settings, and the app hosts the listener itself.

## What Changes

- "Separate window" opens a second Photino window in the same process (created on the UI thread); app-wide services are shared, UiService, frame and pickers are per window; Workspace notices become an event.
- Later in this item: TaskManager.Mcp merges into TaskManager/Mcp, the listener runs in the app (Api/), and --serve, --parent, idle exit, /shutdown and server spawning are removed.
