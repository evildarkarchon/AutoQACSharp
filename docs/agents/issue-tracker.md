# Issue tracker: GitHub

Issues and specs for this repo live in GitHub Issues for
`evildarkarchon/AutoQACSharp`. Use the `gh` CLI for tracker operations.
Run every `gh` command outside the sandbox because authentication uses
the user's Windows Credential Manager. From another directory, pass
`--repo evildarkarchon/AutoQACSharp`.

## Conventions

- Create: `gh issue create --title "..." --body-file <path>`.
  Write multiline bodies to a temporary file first.
- Read: `gh issue view <number> --json number,title,body,state,labels,comments`.
- List: `gh issue list --state open --json number,title,body,labels`,
  adding `--label` or changing `--state` as needed.
- Comment: `gh issue comment <number> --body-file <path>`.
- Apply or remove labels: `gh issue edit <number> --add-label "..."` or
  `--remove-label "..."`.
- Close: `gh issue close <number> --comment "..."`.

Use the label strings in `triage-labels.md`.

## Pull requests as a triage surface

**PRs as a request surface: no.** Set this to `yes` if external pull
requests should enter the triage queue. When enabled, use `gh pr` to
read, list, comment on, label, and close those requests. GitHub shares
issue and PR numbers; resolve an ambiguous `#<number>` before editing.

## Skill operations

Publishing to the issue tracker means creating a GitHub issue.
Fetching a ticket means reading its issue body, labels, and comments.

## Wayfinding operations

- Map: one issue labelled `wayfinder:map`, with Notes,
  Decisions-so-far, and Fog in its body.
- Child: a GitHub sub-issue linked to the map, labelled
  `wayfinder:<type>` (`research`, `prototype`, `grilling`, or `task`).
  If sub-issues are unavailable, link it in the map's task list and
  put `Part of #<map>` in the child body.
- Blocking: use GitHub's native issue dependencies. If unavailable,
  put `Blocked by: #<n>, #<n>` in the child body.
- Frontier: choose the first open, unassigned map child whose blockers
  are all closed.
- Claim: `gh issue edit <number> --add-assignee @me`.
- Resolve: comment with the answer, close the child, and add a gist
  and link to the map's Decisions-so-far.
