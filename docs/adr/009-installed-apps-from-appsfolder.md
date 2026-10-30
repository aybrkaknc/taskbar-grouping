# ADR-009: Adding apps from Windows' installed-apps list

## Status

Accepted (2026-10-31), rule C. The research it rests on was done before any code.

## Context

Apps are added by browsing for an `.exe` or `.lnk`, and `AppEntry.TryCreateFromPath` accepts nothing else. Packaged apps — Calculator, Settings, Sticky Notes, everything from the Store — have neither, so they cannot be added at all. Windows lists every app the user can start, packaged or not, in the shell's *Applications* folder (`shell:AppsFolder`), which is what *Start → All apps* shows.

Measured on the maintainer's machine (Windows 11, 195 entries), 2026-10-31:

- **Enumeration** of `shell:AppsFolder` takes 270 ms, and 234 ms with every item's link target read as well. The target was under 1 s.
- **Identity.** Every item's parsing name is its AppUserModelID, in four shapes: `Family!App` for packaged apps (27); a plain path (14); a known-folder path such as `{1AC14E77-…}\charmap.exe` (89); and a bare AUMID chosen by a Win32 program, such as `Chrome` or an Android app's (65).
- **Targets.** All 168 Win32 items carry `System.Link.TargetParsingPath`, which resolves the known-folder form to a real path; 126 of those targets are `.exe` or `.lnk`, the rest web addresses, `.msc` and `.chm` files. No packaged item has one.
- **Arguments.** 32 of the Win32 items also carry `System.Link.Arguments` — the Android apps (`WsaClient.exe /launch wsa://com.android.vending`), Git Bash (`--cd-to-home`), Event Viewer (`/s`), MMC consoles. Their target alone starts the wrong thing.
- **Launch.** `Process.Start` with `UseShellExecute` on `shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App` starts Calculator within half a second, and returns `null`: shell activation hands back no process. `ProcessLauncher.Launch` treats `null` as a failure today (lines 46–50), so the popup would report every packaged launch as failed. `Path.GetDirectoryName` of such a path is `shell:AppsFolder`, which must not become the working directory.
- **Icons.** `IShellItemImageFactory.GetImage` with `SIIGBF_ICONONLY` returns a 32-bit premultiplied bitmap in 22–62 ms at 48 and 256 px, with a clean alpha channel and no plate behind the icon (Calculator, Settings, Dev Home, Quick Assist, Sticky Notes, NVIDIA Control Panel). `SHGetFileInfo` cannot resolve these items.
- **Icon cache.** `FileSystemIconCache.ComputeKey` uses the file's last-write time, and 0 for a path that is not a file, so a packaged app's cached icon would never change after an update. `System.AppUserModel.PackageFullName` carries the package version (`Microsoft.MicrosoftStickyNotes_6.1.4.0_x64__8wekyb3d8bbwe`) and can stand in for it.

## Decision

**Plain programs by path, the rest through the shell (rule C).** A Win32 item whose target is an `.exe` or `.lnk` and that has no link arguments is stored as that path, exactly as if it had been browsed for: the path is shown, its arguments can be edited, and its icon comes from today's pipeline. Every other app — packaged apps, items with arguments, Android apps, consoles — is stored as `shell:AppsFolder\<AUMID>`, so it starts and looks the way Start shows it.

**The picker lists what starts something.** Items whose target is a web address or a document (`.url`, `.chm`, `.html`, `.pdf`, `.txt`) are left out of the list: they are links and help files, not apps.

Also: `ProcessLauncher` counts a `null` from `Process.Start` as success for `shell:` paths and sets no working directory for them; icons for `shell:` paths come from `IShellItemImageFactory`, through the shell lock; the cache key for them uses the package full name in place of the write time (the cache version stays: no such key was ever written, so nothing stale can be found); the JSON format does not change, since `Path` stays a string.

## Alternatives considered

**A. The target path, with its link arguments, for every Win32 item** (the plan's first draft). Rejected: an Android app's target is `WsaClient.exe`, so the tile would show the Subsystem's icon rather than the app's, and consoles, documents and web entries each need a rule of their own.

**B. Every app as its AppsFolder entry.** One path for everything, launched exactly as Start does it — but a plain program would lose what it has when browsed for: a path to see, arguments to edit, and the icon pipeline every other app uses. Two ways of storing the same program, depending on how it was added, would also surprise.

## Consequences

- `AppEntry` gains a second way in (`TryCreateFromAumid`), and everything that reads `Path` — the display (`PathDisplay`), the default name, the duplicate check, the icon extractor and the launcher — gets a branch for `shell:AppsFolder\` paths.
- Three COM interfaces (`IEnumShellItems`, `IShellItem2`, `IShellItemImageFactory`) are declared in `Core/Interop`, every method in vtable order.
- A group file that names a packaged app does not work on a machine without that package; the popup shows the missing-icon placeholder and the launch fails visibly, as for a deleted `.exe`.
- Nothing new is written outside the program folder, so the uninstaller does not change.
