## ADDED Requirements

### Requirement: The collapsed side menu hides until the pointer nears the left edge
When the side menu is collapsed, the system SHALL hide it off the left edge so the page uses the full width, and SHALL show it as an overlay when the pointer is over a thin zone along the left edge or over the menu itself, hiding it again shortly after the pointer leaves. It SHALL stay shown while keyboard focus is inside it, while one of its popups is open, and while a drag is in progress.

#### Scenario: Collapsed menu hidden
- **WHEN** the menu is collapsed and the pointer is elsewhere on the page
- **THEN** the menu is off screen and the page starts at the left edge

#### Scenario: Pointer near the edge
- **WHEN** the menu is collapsed and the pointer enters the left edge zone
- **THEN** the menu slides in over the page without moving the page content

#### Scenario: Menu in use
- **WHEN** focus is inside the menu or one of its popups is open
- **THEN** the menu stays shown
