# ADR-007: Explorer's "Taskbar Groups" menu as per-user registry verbs

## Status

Accepted.

## Context

Users want to send a program or shortcut to a group from wherever they find it — Explorer, the desktop — without opening the Manager or the popup. The natural place is the file's context menu: *Taskbar Groups ▸ \<group\>*.

Windows 11 has two menus. The compact first menu accepts only handlers implementing `IExplorerCommand` from an app with package identity (MSIX or a sparse package), which requires a code-signing certificate and a different installation model. The classic menu (*Show more options*, Shift+F10) still reads static verbs from the registry.

The launcher already adds files given after `--group-id` (see `CommandLineAppAdder`), under a per-group lock, with one popup for a multi-selection.

## Decision

The Manager writes static verbs under **HKCU** for the `exefile` and `lnkfile` classes:

```
Software\Classes\<class>\shell\TaskbarFolders.Groups      MUIVerb="Taskbar Groups"  SubCommands=""  Icon=<launcher>,0
  shell\<NN>-<id>                                          MUIVerb=<group name>
    command  (default) = "<launcher>" --group-id "<id>" "%1"
```

`NN` keeps the entries alphabetical by display name. The submenu is deleted and rewritten as a whole on Manager startup, after a group is created, renamed or deleted, when the list is reloaded from disk, and when settings are saved. A setting (default on) removes it. The installer deletes both keys on uninstall; a portable install has to switch the setting off before its folder is deleted.

## Alternatives considered

**`IExplorerCommand` with a sparse package.** The only way into the compact menu. Needs signing and package identity — a large change to building, installing and updating the app. Possible later, on its own decision record.

**Shortcuts in the *Send to* folder.** One flat entry per group mixed into the system list, no submenu; a folder there would receive copies of the files instead of acting as a menu.

**A COM context-menu handler (`IContextMenu`).** Also limited to the classic menu on Windows 11, and it means an in-process DLL loaded into Explorer — crashes there take Explorer down. Static verbs get the same placement with nothing running inside Explorer.

## Consequences

- Windows 11 users find the entry under *Show more options*; this is documented, not hidden.
- Explorer starts one launcher per selected file. The per-group lock and `PopupPresence` in the launcher are what make multi-selection safe; they must stay.
- The registry is a second copy of the group names. It is only rewritten by the Manager, so a group renamed while the Manager is closed shows its old name until the Manager next starts.
- Icons are not set per group: group icons are versioned files (ADR-006) whose paths change with every edit, and a stale icon path would show a blank. The submenu itself carries the launcher's icon.
