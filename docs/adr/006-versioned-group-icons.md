# ADR-006: Versioned group icons, and updating our own pinned shortcut

## Status

Accepted.

## Context

A group's taskbar tile shows a composite of its first four apps. After the apps changed, sync rewrote `icons/<id>.ico` in place — and the tile kept showing the old apps.

Measured on a real installation: the pinned tile is not our `shortcuts/<id>.lnk` but the shell's own copy in `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\`. Its `IconLocation` still named `icons\<id>.ico,0`; that file had been rewritten with the new image minutes later. Explorer caches a shortcut's icon by path and index, so a file replaced under the same name is never re-read. The same happened after edits in the Manager, which runs the same sync.

## Decision

- Every new image gets a **new file name**: `icons/<id>-<v>.ico`, where `<v>` is eight hex digits of a SHA-256 over the composite's pixels. An unchanged image keeps its name.
- Sync points our shortcut and the Start menu anchor at the new file as before, and then **points the pinned copies at it too**: every `.lnk` in the pinned folder whose AppUserModelID is the group's has its icon location — and nothing else — rewritten in place, followed by `SHCNE_UPDATEITEM`.
- Only after that are the group's older icon files deleted, including the legacy unversioned `<id>.ico`, so a pin never refers to a file that is gone.
- `IAppDataPathProvider.IsGroupIconFile` decides what belongs to a group: exactly `<id>.ico` or `<id>-<8 hex>.ico`, so a group whose id starts with another's followed by a hyphen keeps its files.

## Alternatives considered

**Notify the shell about the rewritten `.ico`.** The cache is keyed by the shortcut's icon location, which did not change; there is no notification that reliably invalidates one entry. A global `SHCNE_ASSOCCHANGED` rebuilds icons system-wide, flickers the desktop and taskbar, and is kept only as a fallback if a versioned path alone is not enough.

**Rewrite our shortcut only and ask the user to re-pin.** The pinned copy would keep the old path, which is exactly the failure.

**Delete and recreate the pinned copy.** That is unpinning and pinning, which Windows reserves to the user and to its own consent dialog.

## Consequences

- The app writes into a folder the shell owns. The rule is narrow: only `.lnk` files carrying a TaskbarFolders group AUMID, only their icon location, saved in place; nothing there is created, renamed or deleted. Tests redirect the folder, and the test constructors of `AppDataPathProvider` can never point at the real one.
- Icon files are no longer at a fixed path; anything that needs a group's icon asks for the newest matching file (`EnsureStartMenuShortcut` does).
- Installations upgraded from an earlier version still have `<id>.ico` and a pinned copy pointing at it; the first sync of the group moves both over.
