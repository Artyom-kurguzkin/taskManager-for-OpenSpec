# Tasks

## 1. Repair

- [x] 1.1 Find the sides: the merge commit's parents (committed markers) or HEAD and MERGE_HEAD (merge in progress), and their merge-base <!-- agent: Claude -->
- [x] 1.2 Rebuild the file with the board's field-wise merge; newer commit wins per field; conflict note on the item <!-- agent: Claude -->
- [x] 1.3 Run it where sync detects markers, before the pause; unrepairable files still pause <!-- agent: Claude -->
- [x] 1.4 Tests with real git repositories in temp folders; hidden-desktop check <!-- agent: Claude -->
