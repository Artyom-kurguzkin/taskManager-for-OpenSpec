## ADDED Requirements

### Requirement: A document shows the images it links to

The document page SHALL show an image that the document links to with a markdown image link when the link names an image file of the repository: a path relative to the document's folder is tried first, then a path from the repository root. A link to a file that does not exist, or to a path outside the repository, SHALL be left unchanged.

#### Scenario: A drawing beside the plan

- **WHEN** a plan in the plans folder contains `![loop](loop.excalidraw.svg)` and that file is in the same folder
- **THEN** the document page shows the drawing

#### Scenario: A path from the repository root

- **WHEN** a document contains `![shot](docs/shot.svg)` and that file exists under the repository root
- **THEN** the document page shows the image

#### Scenario: A missing file

- **WHEN** a document links to an image file that does not exist
- **THEN** the link is left as written and nothing else on the page changes
