## ADDED Requirements

### Requirement: The agent is a contributor, not a title badge
The work item page SHALL show no agent chip in its title row, and cards and list rows SHALL show none either. An agent that stamped the item (`agentBy`) and is not already a contributor SHALL appear after the people in the contributors row as a lilac chip without a remove control; while the item is unreviewed that chip SHALL be the review control (click marks the item reviewed), afterwards it SHALL stay as a plain lilac chip. An agent already listed among the contributors SHALL be shown once.

#### Scenario: Review from the contributors row
- **WHEN** #wi123 is stamped `agentBy: Claude` and the user clicks the lilac "Claude" chip among the contributors
- **THEN** the item is marked reviewed, the chip stays lilac but is no longer the review control, and the title row never had a chip
