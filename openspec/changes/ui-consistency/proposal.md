## Why

The board, the list, the work item page and the note page each dressed the same things differently: a feature was a stone tag in the list, a dashed chip on a card and a bordered chip on the item page; "+ add" was a boxed button here and a text link there; the note page had plain panels where the item page has tiles. The stylesheet had grown by layers of overrides ("host additions", "fixes", "tighter spacing") with dead rules from the original mockup, and three pages repeated the same page plumbing and the same picker menus.

## What Changes

- One vocabulary, defined once: a feature is a dashed chip everywhere; status, label and priority are pills filled with their colour everywhere (`Pill`); every "+ …" that adds something is the same quiet text button; one `.count`, one `.ic` icon button, one `.chip`.
- The note page's three panels are tiles like the work item page's, each with a coloured heading pill.
- Dropdowns are Radzen's (`RadzenDropDown` under the app's `Dropdown`), so every select has keyboard navigation and listbox roles, and looks like the list's filter dropdowns.
- Shared components: `Pill`, `PickerMenu` (a "+ …" button with a filterable list, used for contributors, dependencies, commits and tests on both pages), `BackButton`, `NotFound`, and a `RepoPage` base that owns the workspace subscription, `Load` and `Run`.
- `app.css` rewritten as cascade layers (tokens → base → primitives → layout → nav → board → list → page → overlays) with no override layers, no dead rules and no `!important` except where an inline style must be beaten.

## Capabilities

### Modified Capabilities

- `board`: feature chips in the list look like those on cards.
- `work-item-editor`: field pills and add buttons match the list; the note page uses the same tiles.
