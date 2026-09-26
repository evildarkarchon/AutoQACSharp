# Issue tracker: Local Markdown

Issues and specs live as Markdown files in `.scratch/`.

## Conventions

- One feature per directory: `.scratch/<feature-slug>/`.
- Spec: `.scratch/<feature-slug>/spec.md`.
- Tickets: `.scratch/<feature-slug>/issues/<NN>-<slug>.md`,
  numbered from `01`, one file per ticket.
- Record triage state in a `Status:` line near the top, using
  `triage-labels.md`.
- Append conversation history under `## Comments`.

Publishing means creating the appropriate spec or ticket file.
Fetching means reading its referenced path. Resolve bare ticket numbers
within their feature directory; ask for the feature if ambiguous.

## Wayfinding operations

- Map: `.scratch/<effort>/map.md`, containing Notes, Decisions-so-far,
  and Fog.
- Children use the numbered ticket paths above and a `Type:` line:
  `research`, `prototype`, `grilling`, or `task`.
- Wayfinding tickets use `Status: open`, `claimed`, or `resolved`.
- Record dependencies as `Blocked by: NN, NN`.
- A ticket is unblocked when every listed dependency is resolved.
- Select the lowest-numbered open, unblocked ticket.
- Claim it by saving `Status: claimed` before starting work.
- Resolve it by appending `## Answer`, setting `Status: resolved`,
  and adding a gist and link to the map's Decisions-so-far.
