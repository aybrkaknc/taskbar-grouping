# Architecture Decision Records

Short records of decisions that were expensive to reach and would otherwise be re-litigated — or, worse, quietly reversed by someone who did not know why the current shape exists.

Write one when a decision constrains future work, when a plausible alternative was rejected for a non-obvious reason, or when the reasoning lives only in a commit message.

## Index

| # | Title | Status |
|---|---|---|
| [001](001-wpf-over-winui.md) | WPF over WinUI 3 | Accepted |
| [002](002-per-group-lnk-aumid.md) | Per-group shortcut with a distinct AppUserModelID | Accepted — supersedes part of ADR-001 |
| [003](003-dpi-unit-contract.md) | Device pixels at the Win32 boundary | Accepted |
| [004](004-popup-acrylic-backdrop.md) | Acrylic backdrop behind the popup | Accepted |

## Conventions

- Files are named `NNN-short-slug.md`, numbered in the order they were accepted.
- Status is one of **Proposed**, **Accepted**, **Superseded by ADR-NNN**, or **Deprecated**.
- Records are not rewritten once accepted. When a decision changes, add a new record and mark the old one superseded, so the history of the reasoning survives.
- Add the new record to the index above.

## Template

```markdown
# ADR-NNN: Title

## Status

Proposed | Accepted | Superseded by ADR-NNN

## Context

What forced a decision. Constraints, requirements, and what was true at the time.

## Decision

What was chosen, stated plainly.

## Alternatives considered

Each option, and the concrete reason it was rejected. This is the part future
readers need most.

## Consequences

What this commits the project to, what it rules out, and what now has to be
maintained because of it.
```
