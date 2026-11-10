# Proposal

## Why

One hand-edited `.openspec.yaml` with an unquoted colon in its title made the app die before its window appeared. Users and agents edit these files by hand, so this will happen again.

## What Changes

- A change whose yaml does not parse still shows, with a warning naming the file and the line; writes to its metadata are refused until it is fixed. Notes with a broken frontmatter show their body with a warning. Mixed line endings are read. A startup error is logged, never fatal.
- Every value the app writes is quoted when YAML needs it, so the app itself never produces an invalid file.
- A value broken in the common ways (an unquoted colon or hash) is repaired on read and written back quoted on the next save; the guide tells agents to quote such values.
