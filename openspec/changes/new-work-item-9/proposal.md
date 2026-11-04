# Proposal

## Why

Deleting the empty "workspace" feature was refused because open work items still had delta specs for it, and the message sent the user off to fix it by hand.

## What Changes

- Deleting a feature that open changes still describe in their delta specs goes through: the confirmation names those items, and their delta folders for the feature are deleted with it (a change left without deltas gets `skip_specs: true`, so it stays valid). Archived changes are never touched. A feature whose folder holds other features still refuses.
