# Proposal

## Why

A plan that embeds a drawing with a markdown image link showed the alt text and a broken image in the app. The document page rendered markdown without resolving image links to files, so plans could not carry diagrams.

## What Changes

- The document page shows an image a document links to: a file beside the document first, then a path from the repository root.
- `.svg` files included, so `.excalidraw.svg` drawings show in plans.
- A link to a missing file is left as it is.
