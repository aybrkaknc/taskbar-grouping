# ADR-010: One resident launcher serves every group's popup

## Status

Accepted (2026-11-13). The host, the request protocol, the tray icon and the native click client are implemented. The numbers below were measured on the maintainer's machine, in a Release build, 10 runs each.

## Context

Every click on a pinned tile started a new `TaskbarFolders.Launcher.exe`, which built DI, loaded the group, built the popup window, showed it, and exited when the popup closed (ADR-002 gave each group its own `.lnk`, so each tile starts that exe). That cost ~440 ms from process start to the first frame, and a CPU profile showed where it went:

- the .NET runtime starting: ~115 ms, which only a process that is already running avoids;
- WPF connecting to the desktop compositor on the first visual change: ~80 ms of native waiting, paid by every new process;
- the XAML, HWND, layout and first render: ~230 ms, mostly WPF's first-use costs.

Nothing in the popup's own code was slow, so tuning it gave ~70 ms at most. The only way to remove the rest is for the process to outlive the click.

ADR-002 rejected "a single always-running host process with one tile". That was about one tile; this record keeps one tile and one `.lnk` per group.

## Decision

**The first launcher a click starts becomes the resident launcher**: it stays alive after its popup closes (`ShutdownMode.OnExplicitShutdown`), shows an icon in the notification area (Open, Exit), and listens on a named pipe. Every later launcher process — started by the same tile, an Explorer menu entry or a dropped file — finds the resident one before WPF is loaded (`Program.Main`), sends it one line, waits for `ok` and exits. The resident launcher builds a new popup for each request.

- **Request**: `v1 TAB groupId TAB x TAB y [TAB file]...`, one UTF-8 line (`HostRequest`). x and y are the click's cursor position in device pixels; the sender captures them, as the launcher always did, after making itself per-monitor DPI aware. No JSON or reflection, so a native-AOT client can send it too.
- **Who is the host**: a named mutex `Local\TaskbarFolders.Host`, held for the process's lifetime (the kernel releases it if the process dies, so a crash leaves no stale claim). Pipe `TaskbarFolders.Host.<user>.<session>`, `CurrentUserOnly`: names are machine-wide, and a second logon session must not reach the first one's popups.
- **Acknowledgement**: the server answers `ok` as soon as the line parses, before the popup is built. A host that does not answer within 400 ms is treated as absent: that click falls back to showing its popup from its own process, so a hung host costs one slow click, not all of them.
- **Toggle** (`HostRouter`): a click on a tile whose popup is open must close it, but the popup is already gone when the request arrives — the press closed it (the popup sees it) and the request comes with the release. The press on the popup's own tile is remembered for 1.5 s, and the next request for that group is dropped. A request while the popup is open (keyboard, Win+number) closes it. A request that carries files never toggles: Explorer sends one per selected file, and each is added whether or not the popup is open yet. Requests are handled one at a time.
- **One session per popup** (`PopupSession`): the view model, the file adder and the window are built for each popup and dropped with it; settings are re-read and the theme re-applied each time, so a change made in the Manager shows up on the next click. The services shared between popups stay in the DI container. A new window rather than a hidden one that is reused: the window's close is a state machine (fade, safety timers, `OnClosed` unsubscribes everything) that was never meant to run twice.
- **Pinning** stays a separate short-lived process (`--pin-mode`): `TaskbarManager.RequestPinCurrentAppAsync` pins "the current app", which is the process's AUMID, and the resident launcher has only one. Popups are `ShowInTaskbar=False`, so they need no AUMID of their own.
- **Memory**: five seconds after the last popup closes, the resident launcher collects garbage and trims its working set. A setting, `launcherResident` (Settings → *Keep the launcher running…*), turns the whole thing off and restores one process per click.

## Measured

| | One process per click | Resident launcher |
|---|---|---|
| Click to first frame (process start of the clicked exe to `ContentRendered`) | ~439 ms | **~94 ms** (72–121) with the native client; ~120 ms (105–150) when the launcher itself forwards the click |
| of which the exiting client | — | ~37 ms native AOT; ~73 ms the self-contained launcher |
| Memory while a popup is open (working set) | ~157 MB¹ | ~75–82 MB |
| Memory while idle (working set, 9 s after the last popup closed) | — (no process) | **~10 MB** |

¹ A fresh process, including first-start extraction; steady-state ~75 MB.

Idle private memory (commit) stays ~84 MB; Task Manager's *Memory* column shows the working set.

## Alternatives considered

**Per-group resident processes.** Keeps the AUMID model but multiplies the memory by the number of groups, and the click would still start a process.

**Reusing one hidden `PopupWindow`.** The fastest option (~30 ms estimated), rejected for now for the reason above; to be reconsidered with measurements of what the new window costs (HWND creation, ~70 ms, is the part it would save).

**A per-window AUMID on one process.** Unnecessary for windows with no taskbar button, and untested for pinning.

**Warming WPF on a helper thread only.** Kept (it overlaps the compositor connect with startup) but it can only trim ~70 ms from a process that still has to start.

## Consequences

- This is the first inter-process channel in the project (ADR-005 chose to have none). Its failure modes are bounded: no host → the click starts one; a host that does not acknowledge → the click shows its own popup.
- The click still starts a process, now a 2 MB native-AOT one (`TaskbarFolders.Click`, same wire format, ~37 ms start to exit as measured from PowerShell). It sits next to `TaskbarFolders.Launcher.exe`; `ILauncherPathResolver.TryResolveClick` finds it there, and `GroupSyncService`, the Start menu anchor, the Explorer verbs and (by AUMID) the taskbar's pinned copies target it. Without it, shortcuts start the launcher, which hands the click over itself, only slower. Publishing it needs the C++ build tools (Visual Studio 2022 Build Tools) and `vswhere.exe` on `PATH`.
- A running host holds `TaskbarFolders.Launcher.exe` open. `ProcessInstanceCloser` already ends processes by that name before an update or uninstall, so the host must keep it.
- A dispatcher exception no longer ends the launcher: it is logged and the host keeps running. If the host dies, the next click starts a new one.
- A setting changed in the Manager applies from the next popup; `launcherResident` itself only when the launcher next starts.
