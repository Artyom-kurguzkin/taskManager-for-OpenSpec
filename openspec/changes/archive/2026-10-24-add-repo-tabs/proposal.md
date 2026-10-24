# Proposal

## Why

The app can only have one repository open at a time; switching to another means Settings → Repository → Open, which replaces whatever was open. Working across several projects meant losing the first one's place every time.

## What Changes

- The side nav gets a tab switcher: every repository you've opened this way stays listed, one click switches the active repo, and you can close a tab you're done with (except the last one). The set of open tabs is remembered between sessions.
- Only the active tab's data is loaded at a time — switching is a fresh, fast read of that repo's files, not simultaneous background state for every open tab.
