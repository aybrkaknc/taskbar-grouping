# Plan

The agreed roadmap for this fork: what is done, what is in progress, and the order of what
comes next. This file is kept current — an item is marked done in the same commit that
finishes it, and a change of order or scope is recorded here when it is agreed, not later.

*Last updated: 2026-10-28*

## Where we are

- **Branch:** `feature/manager-header`, off `develop`. Holds the group header and the themed
  menus; not yet merged into `develop`.
- **`develop`:** everything up to and including the reworked group list, merged locally
  (`cd601bf`). **Not pushed** — `origin/develop` still has only the README fork notice.
- **`origin`:** `feature/send-to-group` (`9ca0245`) and `feature/rename-group-orphan-anchor`
  are the only pushed work branches. The older stacked branches still exist locally; deleting
  them waits for approval.
- **Next:** C6 step 3 — the app list.

## How an item is worked

One item at a time, each on its own branch off `develop`:

1. Tests first, and confirm they fail without the change.
2. The change itself.
3. Documentation per the table in `CLAUDE.md` — `docs/user-guide.md` **and**
   `docs/benutzerhandbuch.md`, `CHANGELOG.md`, and this file.
4. `dotnet format`, then commit.
5. Rebuild `publish/` and stop for a manual test before starting the next item.

Nothing is pushed without asking first.

## In progress

### C6 — Manager redesign

Agreed as a series of small, separately tested steps rather than one rewrite. The skeleton
comes first so that C1–C4 land in their final place instead of being laid out twice.

- [x] **Group list (sidebar)** — taskbar icon and app count per group, accent bar for the
  selection, **+ New group**, per-row context menu, F2/Del. `d7d1abd`, merged into `develop`.
- [x] **Group header** — name edited in place, **Pin to taskbar** as the one accent button,
  *⋯* menu for *Show shortcut…*, *Copy group id* and *Delete group…*, icon menu for
  *Change icon…* / *Use app icons*. `b62aa23`, branch `feature/manager-header`.
- [x] **Themed context menus** — no light icon column in dark mode. `88fceff`, branch
  `feature/manager-header`.
- [ ] **App list** — *Apps* and *Appearance* tabs; a slim "add apps" row in place of the large
  drop box (the whole tab accepts dropped files); a small delete icon that shows on hover
  instead of a *Remove* button on every row. *Appearance* holds only the group icon choice
  until C1 + C2 fill it. **Next.**
- [ ] **Settings and empty state** — settings apply as they are changed (no *Save*); with no
  groups, a three-step "get started" view.
- [ ] **Visual polish** — let the Manager's Mica backdrop show through (it is applied but
  hidden under solid brushes), Fluent icons, consistent spacing; retake the screenshots.

## Up next, in order

| # | Item | Notes |
|---|---|---|
| 1 | **C1 + C2 — grid settings per group** | On the *Appearance* tab: rows, columns, icon size (a number, or automatic from rows/columns), spacing, show/hide names — with a live preview of the popup beside them. |
| 2 | **C3 — per-app settings** | Opens from an app's row: display name, arguments, icon via `AppEntry.IconPath`. Upstream #29, #30. |
| 3 | **C4 — reorder apps by dragging** | Grip handle on each row of the app list. |
| 4 | **B3 — keyboard navigation in the popup** | Arrow keys, Enter to launch, visible focus. Upstream #26. |
| 5 | **B5 — theme per group** | On the *Appearance* tab. Upstream #30. |
| 6 | **D — release** | Retake screenshots, merge the remaining branches into `develop`, push (with approval), cut v0.5.0, open an issue for `IsPinningAllowed = false`, delete merged branches. |

## Done

| Item | Commit | Branch |
|---|---|---|
| Adding an app in the Manager no longer closes it | `3fa414a` | merged |
| The popup no longer accepts dropped files (the right-click menu covers it) | `c49e153` | merged |
| A1 — a second tile click closes the popup | `01d16e2` | merged |
| Complete uninstall, per-user setup (ADR-008) | `71fd68f` | merged |
| B1 — pin the popup open | `237ed01` | merged |
| B2 — scroll a group taller than the popup (upstream #32) | `9ca0245` | merged |
| B4 — placeholder for an app without an icon (upstream #27) | `5f77f59` | merged |
| C5 — an image as a group's icon | `419241f` | merged |
| Screenshots retaken | `066d96a` | merged |
| Shell icon extraction serialised (random blank popup tiles) | `66a99af` | merged |
| C6 — group list | `d7d1abd` | merged |
| C6 — group header, themed menus | `b62aa23`, `88fceff` | `feature/manager-header` |
