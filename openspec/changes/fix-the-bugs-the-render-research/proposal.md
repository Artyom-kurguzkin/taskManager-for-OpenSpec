# Proposal

## Why

The research for #wi180 found bugs that do not wait for the restructure. Worklog and NotePage read attachment images without the junction check and size cap. Settings writes the sync stat cache to disk on every render. Archived list rows read config.yaml per row and use the active project's statuses. A pill click in the list also examines the row. A raw NUL byte in BoardPage.razor.cs breaks search tools.

## What Changes

- Worklog and NotePage read images through Ui.ImageSrc (containment, junction check, size cap).
- Sync status is read without writing files.
- Archived list rows use their own project's statuses, read once.
- A pill click no longer examines its row.
- The NUL sentinel in BoardPage.razor.cs is replaced.
