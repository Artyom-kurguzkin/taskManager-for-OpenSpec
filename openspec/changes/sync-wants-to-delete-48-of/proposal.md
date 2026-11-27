# Proposal

## Why

On 2026-11-26 the Business Intelligence And Finance Tools board's sync paused: "This sync would delete 48 of 77 board files on this computer". The owner: OpenSpec work and work-item merges are not supposed to delete anything. The brake stopped it, but a sync that plans to delete most of a board is a defect, and the owner has hit deletions before.

## What Changes

- Find the 48 paths and why each is planned as a delete here (tombstones in the storage, from which device and action; archive moves; merges; prefix or bucket mix-up).
- Fix the cause so syncing or merging never deletes board files the owner did not delete.
- Decide with the owner what a deletion made on another computer does here.
