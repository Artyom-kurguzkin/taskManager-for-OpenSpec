# Proposal

## Why

Anyone who had committed to a change's folder was pinned as a contributor with no way to remove them, so you couldn't take yourself off your own item. "+ add" also only listed people already on other items, so in a repo where you were alone there was nobody to add and no way to type a new name.

## What Changes

- Contributors are a plain editable list. Every contributor can be removed, including commit authors.
- Commit authors only fill in the list for an item nobody has edited yet; once it's edited, the saved `x-rtm.contributors` list (possibly `[]`) is what shows everywhere.
- "+ add" lists everyone who has ever committed to the repo (from git, all branches), plus people on other items and you; you can also type any name and press Enter.
- Dragging a card between contributor columns on the board moves that person, commit author or not.

## Capabilities

### Modified Capabilities
- `work-item-editor`: contributors are fully editable and new names can be typed in.

## Impact

- `GitHistory` (AllAuthors), `WorkItems` (Queries, Metadata), `TaskManager/Components/Pages/WorkItemPage.razor`, `BoardPage.razor.cs`.
