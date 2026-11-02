## ADDED Requirements

### Requirement: Agent changes are flagged
An item an agent added or changed SHALL show an "agent" chip on its card and list row, and each subtask an agent added one in the list, until the user reviews it. The chip SHALL show while `x-rtm` has `agentBy` and no `reviewedAt` at or after `agentAt`; clicking it SHALL mark the item and its agent subtasks reviewed, and a subtask's chip only that subtask. The side menu SHALL offer "agent changes only".

#### Scenario: Chip on a card
- **WHEN** #wi34 has `agentBy: Claude` and `agentAt` and no `reviewedAt`
- **THEN** its card shows an agent chip whose tooltip names the agent and the time, and clicking it writes `reviewedAt` and removes the chip

#### Scenario: Flagged again
- **WHEN** the agent later stamps a newer `agentAt`
- **THEN** the chip is back

#### Scenario: Agent changes only
- **WHEN** the user ticks "agent changes only"
- **THEN** only items with a flagged item or flagged subtask remain
