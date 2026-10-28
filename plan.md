# Plan

The agreed roadmap for this fork: what is done, what is in progress, and the order of what
comes next. This file is kept current — an item is marked done in the same commit that
finishes it, and a change of order or scope is recorded here when it is agreed, not later.

*Last updated: 2026-10-28*

## Where we are

- **`develop`:** everything up to and including C3 and the removal of the per-group theme,
  merged locally (`aaaf1f2`). **Not pushed** since the app list step; `origin/develop` lacks
  C3 and B5.
- **Branch:** `feature/popup-keyboard` — B3, waiting for a manual test; not pushed.
- **Older branches:** the stacked feature branches are all merged into `develop`; deleting them
  is part of D.
- **Next:** C1 — grid settings, part 1.

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
  *Change icon…* / *Use app icons*. `b62aa23`, merged into `develop`.
- [x] **Themed context menus** — no light icon column in dark mode. `88fceff`, merged into
  `develop`.
- [x] **App list** — *Apps* and *Appearance* tabs; a slim **Add apps…** row in place of the
  large drop box (the whole tab takes dropped files); a delete icon on the hovered or selected
  row instead of a *Remove* button on every row, `Del` to remove. *Appearance* holds only the
  group icon choice until C1 + C2 fill it. `90c3ee4`, branch `feature/manager-app-list`.
- [ ] **Settings and empty state** — settings apply as they are changed (no *Save*); with no
  groups, a three-step "get started" view. Ranked #2 below.
- [ ] **Visual polish** — let the Manager's Mica backdrop show through (it is applied but
  hidden under solid brushes), Fluent icons, consistent spacing; retake the screenshots.
  Ranked #6 below.

## Up next, in order

Ranked by importance on 2026-10-28. The C6 steps above that are still open are placed in this
list where they fall.

| # | Item | What it changes and why |
|---|---|---|
| 1 | **C1 — grid settings, part 1** | On *Appearance*: columns, icon size, show/hide names, applied to the popup. Columns are JSON-only today (upstream #29). |
| 2 | **C6 — settings and empty state** | Settings apply as they change (no *Save*); with no groups, a three-step "get started" view. |
| 3 | **D — release v0.5.0** | Version bump, installer and portable build, installer smoke test, CHANGELOG section, release notes; open an issue for `IsPinningAllowed = false`; delete merged branches. Puts the work so far in users' hands before the larger items. |
| 4 | **C2 — grid settings, part 2** | Live preview of the popup next to the settings, and tile spacing. The preview reuses the popup's own tile style rather than a copy. |
| 5 | **C4 — reorder apps by dragging** | Drag rows in the app list. Order matters: the group icon is built from the first four apps, and today the only way to change it is to remove and re-add apps. |
| 6 | **C6 — visual polish** | Let the Manager's Mica backdrop show, Fluent icons, spacing; retake screenshots. |
| 7 | **E — add apps from a list of installed apps (research first)** | Instead of browsing for an `.exe`/`.lnk`, pick from what Windows lists under *Start → All apps*, which also covers Store/packaged apps (Calculator, Settings…) that cannot be added at all today. The research settles the source (the shell's AppsFolder vs. Start menu shortcuts), how a packaged app is stored and launched (`shell:AppsFolder\<AUMID>` rather than a path), and how its icon is read. |

## Done

| Item | Commit | Branch |
|---|---|---|
| C3 — per-app name, arguments and icon (upstream #29, #30); settings close on a click outside | `862bd57`, `b6470f5` | merged |
| B5 dropped — `GroupConfig.Theme` removed (upstream #30) | `ba95eb7` | merged |
| B3 — keyboard navigation in the popup (upstream #26) | see `git log feature/popup-keyboard` | `feature/popup-keyboard` |
| Pushed `develop` and `feature/manager-app-list` to `origin` | `1da190a`, `9f18df6` | — |
| CI sizing test expects the window height Windows allows | `10e8698` | `develop` |
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
| C6 — group header, themed menus | `b62aa23`, `88fceff` | merged |
| C6 — app list: tabs, slim add row, hover delete | `90c3ee4` | `feature/manager-app-list` |
