# ADR-005: The popup may edit its own group

## Status

Accepted.

## Context

Until now the Manager was the only process that wrote group configs. The launcher read `groups/<id>.json`, showed the popup and exited; adding or removing an app meant opening the Manager, finding the group and using its editor.

Users asked to do both from the popup itself: a **+** that adds apps, and an edit mode that removes them. Two things stood in the way:

- After the app list changes, the group's composite icon, its pinnable shortcut and its Start menu anchor have to be regenerated, or the taskbar tile keeps showing the old apps. That work (`GroupSyncService`) lived in the Manager project.
- The Manager keeps every group in memory and saves its copy on each edit. A change written by another process while the Manager is open would be overwritten by the Manager's next save of that group.

## Decision

The launcher becomes a second writer, limited to **the group whose popup is open**.

- `GroupSyncService`, `IGroupSyncService` and `ILauncherPathResolver` move to `TaskbarFolders.Core.Groups`. The launcher registers them with `SelfLauncherPathResolver`, which answers with the launcher's own path, since the launcher is the shortcut target. The Manager keeps its probing `LauncherPathResolver`.
- The popup saves through the same `IGroupConfigStore` (atomic `.tmp`-then-move writes) and then calls `SyncAsync`, exactly as the Manager's editor does.
- Both use one rule for turning a picked file into an app, `AppEntry.TryCreateFromPath`.
- The Manager re-reads the store when its window is activated and rebuilds its list if a group changed on disk, keeping the selection by id.

Between the two writers it is last writer wins. Concurrent edits of the *same* group in both windows within the same moment are not merged.

## Alternatives considered

**Popup asks the Manager to make the change.** The Manager is often not running, and starting it to perform a one-line edit would cost more than the edit; it would also need an IPC channel the project does not have.

**Popup writes the JSON, Manager regenerates the icon later.** The taskbar tile would show the wrong apps until the Manager next ran, which can be never for a user who only uses the popup.

**A file-system watcher in the Manager instead of reload-on-activate.** It would react while the Manager is in the background, mid-way through whatever the user is doing there, and has to deal with partial writes and event storms. Activation is the moment the user looks at the Manager again, which is the moment the change has to be visible.

## Consequences

- Group sync code is shared and must stay free of Manager-only dependencies.
- Any new writer of group configs has to call `SyncAsync` after saving, and anything that caches groups has to re-read them before saving.
- The popup gains UI that opens a dialog it owns. While that dialog is open, every way the popup dismisses itself (outside click, focus loss, the never-activated fallback) has to stand down, or the popup closes under the picker.
- The launcher's DI graph is larger. Measured on the published build, the popup's start-up did not change beyond run-to-run noise.
