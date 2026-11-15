## ADDED Requirements

### Requirement: The quiet wait is capped
The quiet wait SHALL restart with every change but SHALL NOT end later than 15 minutes after the first change that no pass has taken yet; the first-change time SHALL start again when a pass starts. The sync on exit is unchanged.

#### Scenario: A change every minute
- **WHEN** the board changes every minute for an hour
- **THEN** a pass runs 15 minutes after the first change of each stretch, and the wait is never due later than that

#### Scenario: Changes stop
- **WHEN** the changes stop
- **THEN** the pass follows two minutes after the last change
