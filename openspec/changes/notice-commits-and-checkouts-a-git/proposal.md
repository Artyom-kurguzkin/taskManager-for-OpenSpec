# Proposal

## Why

Nothing watches .git. After a commit or checkout, contributors, the uncommitted dots and the last commit stay stale until the next openspec edit. Meanwhile every examine and every board load walks every file under openspec/changes to fingerprint uncommitted work, which is 80 % of an examine click at 460 items.

## What Changes

- GitHistory watches .git shallowly (HEAD, index, packed-refs, refs/; ignores *.lock) and exposes a HEAD version on its Api.
- The uncommitted set and the last commit are recomputed only when that version or the openspec version moves, not walked per call.
- A commit made outside the app refreshes the board.
