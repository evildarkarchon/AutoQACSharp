# Triage Labels

Use these strings in local tickets' `Status:` lines.

| Canonical role | Tracker string | Meaning |
| --- | --- | --- |
| needs-triage | needs-triage | Needs evaluation |
| needs-info | needs-info | Waiting for more information |
| ready-for-agent | ready-for-agent | Fully specified for an agent |
| ready-for-human | ready-for-human | Requires human implementation |
| wontfix | wontfix | Will not be actioned |

When a skill requests a triage role, use its tracker string.
Wayfinding lifecycle statuses are defined in `issue-tracker.md`.
