# Proposal

## Why

- Sorting the board by feature gave no sign of which feature a card belongs to: expanded cards showed the features as one line of faint grey text.
- The expanded card's done/total subtask count was noise next to the date and contributors.
- A feature that comes from a change's specs/ folder, which is every feature of a merged or archived item, had no remove control, so a wrong link could not be undone.

## What Changes

- Expanded cards show each feature as a dashed chip in the normal text colour. Grouped by bucket they show the status instead, as before.
- Expanded cards no longer show the done/total subtask count.
- Hidden columns listed in the side menu show how many items they hold.
- In the list view an item's features stack in a column in their cell instead of stretching the table.
- Every feature chip on the item page has a remove cross. Removing one that comes from specs/ records it in `x-rtm.unlinkedFeatures`, so the item stops listing it and the board stops placing it in that feature's column. The spec files are untouched. "+ add" offers the feature again, and adding it links it back.

## Capabilities

### Modified Capabilities
- `board`: expanded cards show feature chips and no subtask count.
- `work-item-editor`: features from specs/ can be unlinked and linked again.

## Impact

- `WorkItems/` (`Commands.cs`, `Queries.cs`, `Infrastructure/Metadata.cs`, `PublicDTOs/WorkItem.cs`, `WorkItemsApi.cs`), `TaskManager/Components/Pages/WorkItemCard.razor`, `WorkItemPage.razor`, `wwwroot/css/app.css`.
