# Proposal

## Why

In the list view, scrolled into a group (In Review > High priority), the sticky column header (Title, Feature, Label, Updated...) sits some way below the top of the scroll area: there is a clear empty band above it, and the first visible row is cut off under the header. The owner reported it from the real app with a screenshot; the sticky offsets of the breadcrumb, group headers and column header apparently do not add up.

## What Changes

- The list's column header sticks flush under whatever sticks above it (the group breadcrumb), with no empty band, in every grouping and with subgroups.
- No row is drawn under the sticky header.
- A CDP check measures the header's top against the breadcrumb's bottom while scrolled, in light and dark, at 1280 and 1920 wide.
