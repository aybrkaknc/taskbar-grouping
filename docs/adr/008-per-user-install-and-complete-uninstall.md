# ADR-008: Per-user install, and an uninstall that leaves nothing behind

## Status

Accepted.

## Context

Uninstalling removed the program folder, the Run value and the Explorer menu keys, and left the rest: the group tiles on the taskbar, now pointing at a deleted launcher and showing a blank icon; the Start menu folder with one anchor per group; `%APPDATA%\TaskbarFolders` with groups, settings, icons and logs; `%TEMP%\.net\TaskbarFolders.*`, where .NET unpacks the native parts of the single-file executables on every start; and the values Windows records about programs it has run (MuiCache, FeatureUsage, the Compatibility Assistant store). A portable copy deleted by hand left all of that plus the menu entry. Users notice dead tiles and menu entries, and blame the program.

Everything the app touches belongs to the user who runs it: `HKCU`, `%APPDATA%`, that user's Start menu and taskbar. The installer, however, installed into Program Files with administrator rights. Its uninstaller therefore runs elevated, and Inno Setup offers no way to run a step as the original user at uninstall time (`ExecAsOriginalUser` is documented as unsupported there; the `runasoriginaluser` flag exists only in `[Run]`). An elevated process may be a different account — over-the-shoulder elevation — and so clean someone else's `HKCU` and `%APPDATA%`, and shell actions on the user's taskbar from it are not reliable.

Windows has no API for an app to unpin itself. The pinned shortcut's own context menu carries *Unpin from taskbar*, and invoking it by its canonical verb name, `taskbarunpin`, through `IShellItem` → `IContextMenu` works from a normal user process; verified on Windows 11 build 26300 with a pinned group tile, which disappeared together with its file in the pinned-items folder.

## Decision

- **Setup installs per user** (`PrivilegesRequired=lowest`, `{autopf}` = `%LOCALAPPDATA%\Programs`). No UAC prompt, and the uninstaller runs as the user. A setup that finds an all-users install from 0.4.x stops and asks for it to be uninstalled first.
- **One uninstall procedure**, `Uninstaller` in the Manager, run as `TaskbarFolders.Manager.exe --uninstall`. It ends the other TaskbarFolders processes of the session, unpins group tiles (matched by AUMID) through the `taskbarunpin` verb, removes settings-driven integration, each group's artifacts and Windows' records of this copy and of group AUMIDs, and deletes `%APPDATA%\TaskbarFolders`. **User data is always deleted**; the guides say to back up `groups\` first.
- **Callers provide the backstop** for what a running Manager cannot delete — its last log lines and its own `%TEMP%\.net` folder: Inno's `[UninstallDelete]` (after `[UninstallRun] … --uninstall --no-confirm`) and, for a portable copy, the helper script of the amendment below, which also deletes the `Manager` and `Launcher` folders. *(A portable `Uninstall.cmd` did this first; see the amendment of 2026-10-29.)*
- *Settings → Uninstall TaskbarFolders…* starts whichever uninstaller ships with the copy and closes the Manager.

## Alternatives considered

**Keep the all-users install and clean up elevated.** Cleans the wrong account under over-the-shoulder elevation, cannot reliably unpin, and needs administrator rights for an app that never needs them otherwise.

**Leave the user's data on uninstall, or ask.** The request was explicitly for no traces. Asking adds a step every uninstall has to answer, and a silent uninstall would have to guess.

**Delete the pinned shortcut file instead of invoking the verb.** The taskbar keeps its own record of pins and goes on showing a dead tile until Explorer restarts; the verb is what the shell itself does.

**Clean Windows' records by any value mentioning "TaskbarFolders".** Would also hit a second copy elsewhere, or a user's own files of that name. Values are removed only when they name a path inside this copy or a group AUMID.

## Consequences

- Upgrading from 0.4.x needs one manual uninstall of the old all-users copy; its uninstaller does not touch `%APPDATA%`, so the groups survive that step.
- Each user who wants TaskbarFolders installs it for themselves.
- Anything new the app writes outside its program folder has to be removed in `Uninstaller` in the same change, with a test.
- The file logger drops lines whose folder no longer exists instead of re-creating it.

## Amendment (2026-10-29): deleting a group unpins its tile

The same verb also runs when a single group is deleted in the Manager, through `IPinnedTileRemover`: a deleted group's tile otherwise stayed on the taskbar and opened an empty popup. The confirmation says the tile goes too; the tile is unpinned before the group's files are removed; a tile the shell refuses is reported to the user, who unpins it by hand. `ShellTaskbarUnpinner` now tries the verb by name and then the menu item whose canonical verb it is — both language-neutral. After an unpin the Manager offers to restart Explorer, never restarting it unasked. Deleting the pinned file is still not an option, for the reason above. The uninstaller and group deletion remain the only places that remove files from the pinned-items folder, and only through the verb.

## Amendment (2026-10-29): Windows records the uninstall's own processes as they exit

After an uninstall, two values were still in the Program Compatibility Assistant's store (`HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store`): the Manager's and `unins000.exe`'s. The store holds a record for each short-lived program, written when it exits, so those two arrive after `Uninstaller` has finished, and a process cannot delete its own exit record. For an installed copy `Uninstaller` therefore starts a detached `cmd` (`CompatibilityRecordSweep`) as its last step. The script waits (up to two minutes) until `TaskbarFolders.Manager.exe` and `unins000.exe` are no longer running, then deletes the exact values for the Manager, the Launcher and `unins000.exe` from every key in `Uninstaller.WindowsRecordKeys` with `reg delete` — 3 s after the exit and again 5 s after — and then deletes itself. Measured in a real install/uninstall: Windows wrote `unins000.exe`'s record 3.4 s after it exited and the Launcher's 2–3 s after each exit; a single pass at 3 s beat the record by under a second, hence the second pass at 5 s — 1.6 s clear of the worst delay seen. `cmd` and `reg` were chosen over PowerShell, which antivirus products treat with more suspicion; the script is a file in `%TEMP%` because it needs labels and a loop. A failure to start it is logged and does not make the uninstall incomplete. A portable copy used to rely on an `Uninstall.cmd` for this, which meant a console window held open for the delays; the ZIP no longer contains it. *Settings → Uninstall TaskbarFolders…* runs the portable Manager with `--uninstall`, and the helper does what the script did: after the same two passes it deletes the `Manager` and `Launcher` folders, the runtime .NET unpacked into `%TEMP%\.net`, the data folder (the Manager's last log lines), and the extracted folder with a non-recursive `rd`, so a folder holding anything of the user's stays. The helper starts only once `Uninstaller` has run, so a refused confirmation (exit code 2) deletes nothing, as the script's exit-code check ensured. A portable copy is recognised by its `Manager` and `Launcher` folders with their executables and no `unins000.exe` (`InstallLocation.IsPortable`); a development build has neither layout and gets no helper. The sweep also covers the names MuiCache stores with `.FriendlyAppName` and `.ApplicationCompany` appended to the path. The wait is by image name, which is safe because `IInstanceCloser` has already ended every other Manager.
