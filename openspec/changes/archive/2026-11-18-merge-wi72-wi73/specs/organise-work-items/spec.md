## ADDED Requirements

### Requirement: Agent changes are flagged
An item an agent added or changed is flagged while `x-rtm` has `agentBy` and no `reviewedAt` at or after `agentAt`. Cards and list rows SHALL NOT show an agent chip; the flag is reviewed on the work item page. In the list, each subtask an agent added SHALL carry a chip whose click clears that subtask only. The side menu SHALL offer "agent changes only".

#### Scenario: Chip on a card
- **WHEN** #wi34 has `agentBy: Claude` and `agentAt` and no `reviewedAt`
- **THEN** its card and list row show no agent chip, "agent changes only" still lists it, and its page reviews it

#### Scenario: Flagged again
- **WHEN** the agent later stamps a newer `agentAt`
- **THEN** the item is flagged again: the page shows the review control and the filter lists it

#### Scenario: Agent changes only
- **WHEN** the user ticks "agent changes only"
- **THEN** only items with a flagged item or flagged subtask remain
