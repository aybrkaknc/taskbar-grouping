# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Rearrange a group's apps in the popup.** With ✎ on, a tile can be dragged to another place: it follows the pointer, the other tiles slide out of its way, and it settles into its cell when let go. The group's icon is rebuilt when one of the first four apps moved. Letting go outside the tiles or pressing Escape puts every tile back, and a group taller than the popup scrolls while the tile is held near its edge. In edit mode the tiles wiggle, as icons do on a phone, and the header shows only the pencil, at its right end; add, pin and close come back when editing ends. With *Enable animations* off the tiles stay still and everything moves without animation.
- **A preview of the popup on *Appearance*.** To the right of the popup settings, the group's first twelve apps are shown as the popup lays them out, with their icons and names, and follow every change at once. It is drawn from the same tile definition as the popup, so the two cannot drift. An empty group shows sample tiles. When the settings make the popup too wide for the space, the window grows to make room, up to the screen's width, and the settings column scrolls if the window is short. The window now opens at 1180 × 720 (smaller where the screen is) and is no narrower than 900 × 520.
- **Spacing between the popup's tiles.** *Appearance → Popup → Spacing* sets the gap between the tiles to *None* (the default, as before), *Small*, *Medium* or *Large*. The tiles keep their size; the popup grows by the gaps, and its largest width went from 720 to 800 so six of the widest spaced tiles still fit. Groups saved earlier keep the tiles touching.

### Fixed

- **Some apps' icons no longer show as a tiny glyph.** Programs and shortcuts with no large icon of their own (an older program that has only a 48 px icon, a shortcut to one) came back from Windows as a small icon in the corner of a large empty square, and drew as a small mark in the middle of their tile, in the group's icon and in the popup. The icon is now cut back to its own size and scaled like any other. Icons are cached, so the cache was versioned: those apps get the corrected icon the next time they are read, with nothing to delete by hand.
- **A shortcut shows its own icon.** A `.lnk` was shown with its target's icon, so a shortcut that runs Java but wears the program's own icon (UniFi) came out as Java's. The Manager and the popup now use the icon the shortcut was given, as Explorer does, whether that is an `.ico` file or an icon inside a program or DLL; a shortcut with no icon of its own, or whose icon is missing, still shows its target's.

## [0.5.1] - 2026-10-29

### Fixed

- **Explorer's "Taskbar Groups" menu shows the app's icon.** It showed a generic program icon, because the launcher it pointed at had none of its own. The menu now takes the Manager's icon, and the launcher carries the same one. The menu picks it up the next time the Manager starts.
- **Pressing `Enter` in the group name box shows that the name was saved.** The box kept its focus border, so nothing showed the change had been accepted; `Enter` now moves the focus to the group list after saving, as `Esc` already did.
- **Deleting a group no longer logs a warning when no group icon was ever chosen.** The folder for chosen icons does not exist until one is picked, and removing the icon of a group that has none was logged as "Could not delete the custom icon". Nothing was wrong on disk; the line only made the log look like something had failed.
- **Deleting a group unpins its taskbar tile.** The tile used to stay behind and open an empty "No apps to launch" popup. The confirmation now says the tile goes too; afterwards the Manager asks whether to restart Explorer so the taskbar redraws, and says so if Windows refuses to unpin the tile. Tiles left by groups deleted with earlier versions are listed once, the first time a group is deleted. Unpinning now also falls back to the shell's menu item when the verb is refused by name, which the uninstaller benefits from too.
- **When Windows will not take a pin request, the Manager says why and offers what works.** The most common cause is turned-off notifications: Windows asks for approval through a notification and shows none while they are off. The Manager now offers to open the notification settings, or to pin by hand. A Windows 11 build that still requires a Limited Access Feature key and a policy that turns pinning off get their own messages. The shortcut folder now opens with the shortcut selected. The old message blamed the Windows build in every case.
- **A pinned group cannot be pinned again.** While a group is on the taskbar, *Pin to taskbar* reads *Pinned* and is turned off; its tooltip says how to pin it again. Pinning an already pinned group made Windows redraw its tile with a blank icon. The button turns back on when the Manager is switched to after the tile was unpinned by hand.
- **Explorer's "Taskbar Groups" menu is rewritten one change at a time.** Deleting or creating a group could start two rewrites at once; one then failed with a "registry key does not exist" warning in the Manager's log, and the menu could be left listing the groups as they were a moment before.
- **Uninstalling leaves no Compatibility Assistant records behind.** Windows notes the Manager and the installer's uninstaller in the registry as they exit, after the cleanup had run, so two values stayed. A short-lived helper now deletes them once both have exited, 3 and 5 seconds after, which also covers the names Windows' MuiCache stores for the Manager.
- **Pin to taskbar asks Windows again before saying pinning is unavailable.** Right after a group was created Windows could answer "not allowed" and, a few seconds later, allow the same request; the first press then offered only the manual way. The launcher now asks up to six times, a second apart. Where pinning really is blocked (notifications off, a policy), the explanation now appears about five seconds after the press instead of at once. Windows' own approval is untouched.
- **A portable copy uninstalls through Settings only; `Uninstall.cmd` is gone.** The script held a console window open while it waited for Windows' records. *Settings → Uninstall TaskbarFolders…* now runs the Manager's own uninstall, and a background helper removes the `Manager` and `Launcher` folders a few seconds after it exits. To uninstall a copy you no longer have open, extract the ZIP again, start its Manager and use the same button.

## [0.5.0] - 2026-10-28

### Added

- **Each group sets out its own popup.** The Manager's *Appearance* tab has a *Popup* section with the most **columns** per row (1–6), where a group with fewer apps uses only the room they need; the **icon size** (Small, Medium, Large, Extra large) and **Show app names**. The tiles grow with the icon so names still fit, and without names they shrink to the icon. Changes save as they are made and need no sync, since the taskbar icon does not show them. Groups saved before keep their look: 3 columns, medium icons, names shown. The column count could until now be changed only in the group's JSON (upstream #29). Icons are now read at twice their displayed size, so they stay sharp at 150 % scaling.
- **The popup works from the keyboard.** The first app holds the focus when the popup opens, so `Enter` starts it without a click; the arrow keys move between apps (left/right across row ends, up/down within the column, wrapping round), `Home`/`End` go to the first and last, and the focused app gets an accent outline. The outline is drawn only for keyboard focus, so opening the popup with the mouse looks as before (upstream #26).
- **Each app can have its own name, launch arguments and icon.** Clicking an app in the Manager's list, or the arrow on its row, opens its settings under the row; clicking it again, `Esc` or a click anywhere else closes them, and only one app's are open at a time. The **name** is what its tile says (cleared, it goes back to the file name); **arguments** are passed when it starts, e.g. a second browser profile; **Change icon…** takes an `.ico`, `.png`, `.jpg` or `.bmp`, which then shows in the popup, the list and the group's icon, and **Use program icon** goes back. A copy of the image is kept in `icons\custom\apps\` and removed once no app uses it, or with the group. Names and arguments save when you leave the box or press `Enter`; `Esc` puts the saved value back. Until now the arguments could be set only by editing the JSON, and an app's `iconPath` was stored but never read (upstream #29, #30).
- **A group's icon can be an image of your choice.** *Change icon…* under the preview in the Manager takes an `.ico`, `.png`, `.jpg` or `.bmp`; a copy, fitted into a square, replaces the icon built from the group's apps on the shortcut, the Start menu entry and a pinned tile. *Use app icons* goes back to the built icon. The chosen image is kept in `icons\custom\<id>.png` and deleted with the group.
- **The popup can be pinned open.** A pin in its header keeps it on screen while you click elsewhere, switch windows or launch apps, so several apps can be started in a row. ✕, Escape and a click on the group's tile still close it; every popup opens unpinned.
- **Uninstalling leaves nothing behind.** The installer's uninstaller, a new `Uninstall.cmd` in the portable ZIP and *Settings → Uninstall TaskbarFolders…* all run one procedure (`TaskbarFolders.Manager.exe --uninstall`): it closes running copies, **unpins the group tiles**, and removes Explorer's *Taskbar Groups* menu, the start-with-Windows entry, the Start menu entries, the records Windows keeps about the program, `%TEMP%\.net\TaskbarFolders.*`, and **the user's groups and settings** in `%APPDATA%\TaskbarFolders`. The portable script then deletes its own files, and deletes nothing at all if the user cancels or the Manager fails. Uninstalling used to leave dead tiles with blank icons, the Start menu folder and all user data; deleting a portable copy also left the menu entry pointing at a missing program (ADR-008).
- **"Taskbar Groups" in Explorer's right-click menu.** For `.exe` files and shortcuts, Explorer's classic menu (*Show more options* on Windows 11, or Shift+F10) offers *Taskbar Groups ▸ \<group\>*; choosing a group adds the file and opens its popup, and a multi-selection is added in one go. The Manager keeps the per-user registry entries in step with the groups, a new setting (on by default) removes them, and the installer deletes them on uninstall (ADR-007).
- **The launcher adds files named on its command line.** `TaskbarFolders.Launcher.exe --group-id <id> <file>…` adds every `.exe`/`.lnk` not already in the group, saves and syncs it, and opens the popup showing them. It is the path Explorer's menu takes. Several launchers started together for one group (a multi-selection) add their files under a per-group lock so none is lost, and only one of them shows a popup; the others make it reload.
- **Apps can be removed from the popup.** ✎ in the header switches the popup into edit mode: every tile gets a ✕ that takes the app out of the group (the file on disk is not touched), tiles stop launching, and the group is saved and its taskbar icon regenerated after each removal. ✓ or Escape leaves edit mode; Escape now backs out of edit mode before it closes the popup. Removing the last app shows the empty state, from which + can fill the group again.
- **Apps can be added from the popup.** A + in the popup's header opens a file picker; every `.exe` or `.lnk` chosen is added to the group, appears in the popup at once, and the group's taskbar icon and shortcuts are regenerated as they are from the Manager. The popup stays open while the picker is: its outside-click, focus-loss and fallback dismissals stand down for the dialog's lifetime. The popup now writes its own group's config, recorded in ADR-005.
- **The popup closes on any click outside it, on Escape, and fades as it goes.** A click outside used to close the popup only through focus loss, which never happens to a popup Windows did not activate; it then lingered until a three-second fallback. A low-level mouse hook now sees the click either way. Escape closes it too. Every way of closing ends in one place that plays a 120 ms fade and shrink of the content when *Enable popup animations* is on; on Windows 11 22H2+ the acrylic panel itself cannot be faded and disappears right after the content.
- **The popup names its group and has a close button.** A header row shows the group's name on the left and a ✕ on the right that closes the popup without launching anything. The "no apps" message no longer repeats the name. The header is part of the measured popup chrome, so the window grows by its height with no sizing code of its own.
- **The popup has a glass background.** On Windows 11 22H2 and later the tiles sit on DWM acrylic, a blurred translucent panel tinted by the theme with rounded corners, instead of floating directly on whatever is behind them. Older Windows gets a translucent fallback panel. This reverses the fully transparent popup of v0.3: the group read poorly as one surface, and the labels' legibility depended on the content underneath. A system backdrop never paints behind a layered window, so on 22H2+ the popup is now a non-layered window; the reasoning is recorded in ADR-004.
- **Groups can be renamed.** The name at the top of the detail pane is now editable; **Rename** or `Enter` saves it. The sidebar re-sorts and the shortcut and Start menu entry follow the new name. Only the display name changes — the id, and with it the AppUserModelID, stays — so a tile already pinned keeps working. Until now the only way was to edit the group's JSON with the Manager closed (#25).

### Changed

- **Settings apply as you change them.** The Settings dialog has no **Save** button and no *Unsaved changes* marker any more: the theme switches, the start-up entry is written and the Explorer menu is rewritten the moment you click, and **Close** only closes the dialog. Changes are applied one at a time, so two quick clicks cannot undo each other. If the start-up entry cannot be written, a message says so and the checkbox goes back to what Windows really has — before, a failed save could leave the box showing a setting that was not in effect.
- **A first launch explains itself.** With no groups, the Manager shows three steps — create a group, add your apps, pin it — and a **+ New group** button, in place of an empty editor with a one-line hint.
- **Group files no longer carry a `theme`.** The per-group theme was written to every group file but never read — both the Manager and the popup follow the theme in Settings — and is not going to be built. Files that still have the field load as before and lose it on their next save (upstream #30).
- **The group's apps take the room.** The detail pane has two tabs. *Apps* opens with a slim **Add apps…** button in place of the large drop box — the whole tab takes dropped files, outlined while something is dragged over it — and a list whose delete icon appears only on the row under the mouse or the selected one, instead of a *Remove* button on every row; `Del` removes the selected app. *Appearance* holds the group icon choice for now and is where the grid settings will go. The tabs are themed (WPF's own draw white faces) and their content is visible to UI Automation, so screen readers reach the app list.
- **The Manager's group header is simpler.** The name is edited in place and saved on `Enter` or when you click away; `Esc` puts it back, and a blank name is never saved. The **Rename** button, the group id under the name and the always-visible pinning explanation are gone — the id is under *⋯ → Copy group id*, and the explanation is the **Pin to taskbar** tooltip (a failed pin still says what to do and opens the shortcut folder). **Pin to taskbar** is the one accent-coloured button; *Show shortcut…* and *Delete group…* moved into the *⋯* menu beside it. Clicking the group's icon offers *Change icon…* and *Use app icons*, which replaced the two buttons under the preview. The app list now starts right below the header.
- **The Manager's group list is easier to read and to work with.** Each group shows the icon it has on the taskbar (a folder until it has apps) and its number of apps; the selected group is marked by a short accent bar instead of a full-width block. **+ New group** creates a group called "New group" and puts the caret in its name, so nothing has to be typed before the button works — the name box and the greyed-out **+ Add** are gone. The context menu offers *Rename* and *Delete group* for the row it was opened on; a right-click on empty space used to delete whichever group was selected. **F2** renames and **Del** deletes the selected group. *Settings* moved to the foot of the list.
- **Setup installs for the current user.** It installs into `%LOCALAPPDATA%\Programs\TaskbarFolders` without asking for administrator rights; everything the app touches already belonged to the user, and only an uninstaller running as that user can unpin their tiles and clean up after it. A setup that finds an all-users install from 0.4.x asks for that one to be uninstalled first (ADR-008).
- **The Manager picks up changes made while it was in the background.** When its window is activated it re-reads the groups and, if any of them changed on disk, rebuilds the list and keeps the same group selected. Previously a group edited elsewhere while the Manager was open was silently overwritten by the Manager's next save. Nothing is reloaded when nothing changed, so a rename being typed survives switching windows.
- **The popup is visible from its first frame.** The open animation faded in from fully transparent over 200 ms, and it could only start once the first frame had been drawn — several hundred milliseconds after the click. That first frame therefore showed nothing, and the popup became recognisable only part-way through the fade. It now starts half-opaque at 90 % size and settles in 110 ms, so the popup is on screen with the first frame and finished about 90 ms sooner. The 500 ms safety net that snaps a stuck popup to its final appearance now triggers on anything short of that appearance; with a half-opaque start, its old "still nearly invisible" test would never have fired again. Turning *Enable popup animations* off behaves as before.

### Fixed

- **Screen readers can name every button.** Eight buttons whose content is an icon, or an icon and a label, had no accessible name and were read out as just "button": *Settings*, *Pin to taskbar*, *Add apps…*, the group icon, *More*, each app's remove and settings buttons, and every tile in the popup. They now carry names, the row buttons and tiles including the app's ("Remove Notepad"), and a test fails for any new button without one.
- **Closing an active popup no longer ends the launcher with an error.** When the popup was closed while it had the focus — `Escape`, ✕, or launching an app — Windows deactivated it as it was being destroyed, and the popup answered by closing itself a second time, which threw. Nothing showed, but the launcher exited with code 4 and wrote an *Unhandled dispatcher exception* to its log. A deactivation that arrives once the close is under way is now ignored.
- **Menus follow the theme.** The Manager's context menus were drawn by WPF's own template, which paints a light icon column and divider down the left edge in fixed colours — a white strip in dark mode. The menu, its items and its separators now have themed templates: no icon column, the theme's colours, rounded corners, and the shortcut key (F2, Del) shown in the subtle text colour.
- **Every app in the popup gets its icon.** The popup extracts its apps' icons in parallel, and the shell, called from several threads at once, returned no icon for most of them: tiles stayed blank (since the placeholder below, showed it) at random, more often the first time a group was opened, and filled in over later openings as the icons that did come through were cached. The extractor now makes its shell calls one at a time; the tiles still appear first and their icons stream in.
- **An app without an icon gets a placeholder instead of a gap.** Executables that ship no icon left an empty space in the popup and the Manager's list, dropped out of the group's composite icon, and a group made only of such apps got no shortcut and no tile at all. They now show a plain window outline: in the popup and the Manager in the theme's subtle text colour, in the composite icon in a grey that reads on a light and a dark taskbar. The icon extractor still reports "no icon"; the placeholder is drawn where icons are shown, so a real icon that turns up later replaces it (upstream issue #27).
- **Logging can no longer fail the caller.** A log line whose folder had been deleted threw from the file logger; the Manager's shutdown then reported an error. Such lines are now dropped, and the folder is not re-created.
- **A damaged group file no longer stops the Manager from starting.** One `groups\*.json` that was not valid JSON — a hand edit with a stray backslash is enough — threw out of loading the group list and crashed the Manager on every start. That file is now skipped and stays on disk to be repaired; the other groups load.
- **Every app in a large group can be reached.** Past eight rows — 24 apps at three columns — the popup cut the grid off at its maximum height with nothing to show more apps existed (upstream issue #32). The tiles now scroll, with a thin bar in the theme's colours; the window widens by the bar so the tiles keep their size, and a group that fits looks as before.
- **Clicking a group's tile again closes its popup.** The click used to close the popup and then open a fresh one, because the press on the tile counts as a click outside the popup and the shell starts a new launcher for it. A popup closed by a press on the tile that opened it now leaves a short-lived per-group marker, and the launcher started by that click finds it and shows nothing.
- **Adding an app in the Manager no longer closes it.** Closing the file picker re-activates the Manager, which re-reads the groups from disk; that re-read could land while the add was still saving, see the file from before the save, replace the group being edited and leave the editor with nothing bound — and the add then crashed the Manager on its next step, with the app already saved. The re-read now stands down while the editor is saving, and an add or remove finishes for the group it started on.
- **An app added from the popup appears whole at once.** A tile that opened a new row arrived cut off — the popup was resized before the new tile had been laid out, so it kept its old height and the new row stayed outside it until the popup was reopened. Resizing now waits until the layout includes the new tile.
- **The pinned tile shows the group's current apps.** After apps were added or removed — from the popup or the Manager — the taskbar tile kept the old composite icon. The icon file was rewritten correctly, but under the same name, and Windows caches a shortcut's icon by path; the tile is the shell's own copy of our shortcut and kept pointing at that path. Each new image now gets a new file name (`<id>-<hash>.ico`), sync points our shortcuts and the taskbar's pinned copies of the group at it, and only then deletes the old versions. Only the icon location of pinned shortcuts carrying a group's AppUserModelID is ever changed (ADR-006).
- **The Manager reports what actually happened to a pin.** It said "Pinned" after every attempt: when Windows refused pinning outright, when the user declined, when the pin failed, and when no tile appeared. The launcher computed the right exit code and logged it, but closing its host window — the last window — made WPF start a shutdown with code 0, and the `Shutdown(exitCode)` that followed was ignored. So the "Pin not available" and "Pin not confirmed" paths added in v0.4.8, and the shortcut folder they open for a manual pin, never ran. Pin mode now uses explicit shutdown. Verified by running pin mode where Windows disallows pinning: the process exit code went from 0 to 2.
- **Popup labels follow the theme.** The app names were black in both themes, so in dark mode they were black text on a black halo. The tiles are buttons, and WPF's built-in button style sets the text colour explicitly, which outranks the theme colour inherited from the popup. The tile style now sets it itself — the same precedence rule v0.4.7 fixed in the Manager.
- **Renaming a group no longer leaves a second Start menu entry behind.** The entry is named after the display name while its identity is the group's AppUserModelID, and nothing removed the entry written under the old name — two Start menu entries ended up carrying one identity, a plausible cause of pins that report success and produce no tile. Entries are now found by reading their AppUserModelID back rather than by file name: writing a group's entry removes any other carrying the same identity, deleting a group removes all of them whatever name they were written under, and Manager startup removes entries whose group no longer exists. A group whose JSON fails to parse keeps its entry, since its tile may still be pinned (#28).

- **Popup height grows with the launch-failure strip.** When a launch failed, the red error strip docked to the bottom of the popup without changing `Window.Height`, so it stole space from the fixed 96 px tiles and clipped the bottom row. Height now comes from measuring the popup chrome, clamped to `MinHeight`/`MaxHeight`, and placement is refreshed so the taller popup still clears the taskbar.

### Internal

- **Group sync lives in Core.** `GroupSyncService`, `IGroupSyncService` and `ILauncherPathResolver` moved from the Manager to `TaskbarFolders.Core.Groups`, with their tests, so the popup can regenerate a group's icon and shortcuts after editing it; the Manager keeps its own `LauncherPathResolver`. The rule for turning a picked file into an app entry (`.exe`/`.lnk`, named after the file) moved out of the Manager's editor into `AppEntry.TryCreateFromPath` for the same reason. No behaviour changes.
- **The popup window tests no longer fail at random.** Since the popup attaches a `WindowChrome` on Windows 11 22H2+, constructing two popups at the same moment on different threads could corrupt a dictionary WPF keeps for the whole process without locking. The launcher has one UI thread and never does that, but the test host builds each window on its own STA thread and xUnit ran the three window test classes in parallel, so roughly one full run in three failed whichever window test came first. They now share one xUnit collection and run in sequence; eight consecutive runs passed.
- The launcher's startup timing says where the popup's time goes after the group is loaded. The existing line stopped at `vm` and then jumped to `loaded`, leaving the largest block — roughly 300 ms of building and showing the window — as a single number. It now carries `window` (the popup object exists) and `placed` (native window created, size and position computed), and a second line, `First render`, written from `ContentRendered`, covers what happens after `Show()` returns: `shown`, `activated`, `icons` and `rendered`, plus `runtime` for the share spent starting .NET and WPF before any launcher code ran. `processAge` was documented in the code as the time up to the launcher's first line; it is, and always was, the time up to the moment the line is written. Both lines are described in `docs/troubleshooting.md`. Diagnostics only — nothing about the popup's behaviour changes.
- The XML documentation rule is enforced rather than asserted. `.editorconfig` set `CS1591` to `warning`, but no project generated a documentation file, so the compiler never emitted the diagnostic and two documents claimed an enforcement that did not exist. `Directory.Build.props` now generates one for the `src` projects; the three test projects switch it off, where xUnit's public classes would demand hundreds of comments that document nothing. No documentation had to be written — all 349 publicly visible declarations in `src` already had it. Proven both ways: an undocumented public member fails the build, and removing a test project's opt-out produces 268 errors.
- `CONTRIBUTING.md` now says which commit trailers are unwanted, which it never did — the rule lived only in the internal notes, where a contributor could not be expected to find it, and was worded broadly enough to read as a ban on crediting human co-authors. It is not: tools are not co-authors, people are.
- The popup's effective ceiling — eight rows, 24 apps at three columns, and no scrolling past it — is recorded as a known limitation and tracked as an issue, rather than left for a user to discover by losing tiles.

## [0.4.10] - 2026-09-03

Housekeeping release. Nothing about the application's behaviour changes; what changes is that the project can be handed to someone else without a tour.

### Fixed

- **The coverage report measures something.** Release set `DebugType=none`, so coverlet had no debug information to instrument against and every CI coverage report was empty — uploaded as an artifact and summarised over nothing, for the pipeline's entire life. Release now uses `DebugType=embedded`, which keeps collection working without emitting separate `.pdb` files that would have to be excluded from the published artifacts. Measured after the change: **69.2 % line, 51.9 % branch** across four assemblies. CI now fails below 65 %, and also at exactly 0 % — an empty report is indistinguishable from an untested code base, which is the failure this fixes.
- **Release pages carry the changelog.** `generate_release_notes` builds its text from merged pull requests and this project cuts releases from direct pushes, so every release page showed a bare compare link while `CHANGELOG.md` held the actual prose. The workflow now lifts the matching section out and uses it as the release body. The thirteen existing releases were backfilled from the same source.

### Changed

- **The project is attributed to its author.** `Authors`, `Copyright`, the `LICENSE` and the installer's publisher field all read "TaskbarFolders Contributors"; the last of those is what Windows shows in the setup wizard and in Apps & features. The security and conduct contacts, previously placeholders, now name a real address.

### Internal

- A **Definition of Done** in `CLAUDE.md`, mirrored in `CONTRIBUTING.md` and the pull request template, covering the practices this release had to establish: evaluate the format gate rather than announce it, look at visual changes instead of asserting them, execute interop paths the headless suite cannot reach, retake screenshots when the UI moves, and prove a new gate fires by feeding it a failing input.
- `ci.yml` gains `main` and `docs/*` as push triggers, so the release-time fast-forward is verified and a required status check has a run to attach to.
- Seven of the documented limitations are now issues with file references and acceptance criteria, rather than prose in `docs/troubleshooting.md`.

## [0.4.9] - 2026-09-03

Presentation release: the app no longer puts your Windows account name on screen, and the README finally shows the application.

### Changed

- **The README and both user guides now show the application.** A UI project whose documentation contained no interface was asking readers to imagine it: a short animation of a tile opening its popup, the Manager in both themes, the chrome-less popup, and the settings dialog.
- **App paths are shortened for display.** The editor showed each entry's full path, which put the user's account name on screen for every app installed under their profile — noise in normal use, and a small privacy leak in any screenshot, screen share or bug report. Anything inside the user folder now displays as `%USERPROFILE%\…`, with the full path on hover. Only the display changes; what is stored and launched is unaffected, and paths outside the profile such as `C:\Program Files\…` or `C:\Users\Public\…` are left alone.

## [0.4.8] - 2026-09-03

Pin to taskbar has never worked. This release fixes it, and makes the application stop claiming success it cannot demonstrate.

### Fixed

- **The pin button pins.** Every attempt since v0.4.0 threw `InvalidCastException` before reaching the pin call: `TaskbarPinRunner` called `InitializeWithWindow.Initialize` on the `TaskbarManager`, which does not implement `IInitializeWithWindow` — that interop rule applies to WinRT types owning a modal surface, such as `FileOpenPicker`. Everything after that line was unreachable, including the v0.4.2 diagnostic meant to explain pin failures and the `SHChangeNotify` settle work, neither of which had ever run. The catch mapped the exception to exit code 3, so the user got a "Pin failed" toast and an Explorer window on every click. `RequestPinCurrentAppAsync` parents its own dialog and takes no window handle; what it needs is for the process to hold the foreground, which the runner now re-asserts and logs when Windows refuses.
- **The AUMID stamp no longer fails silently.** `SetCurrentProcessExplicitAppUserModelID`'s HRESULT was discarded, so a failed stamp would have pinned the wrong identity with nothing to say so.

### Added

- **A reported pin is verified rather than believed.** `RequestPinCurrentAppAsync` has been observed returning success while persisting nothing, and telling someone "Pinned" when no tile appeared is indistinguishable from a broken application. The launcher now looks for a pinned shortcut carrying the group's AppUserModelID and reports **Pin not confirmed** when there is none, opening the shortcut folder so it can be pinned by hand. Matching is by AUMID rather than file name because Windows names the pinned copy itself — a programmatic pin arrives under the Start-menu anchor's name, a manual one under the group id. A folder that cannot be inspected is reported as inconclusive, never as a failure.
- `IShortcutReader` in `TaskbarFolders.Core` — the read counterpart to `IShortcutGenerator`, returning `null` rather than throwing.

### Internal

- **Test platform bumped across all three test projects**: `Microsoft.NET.Test.Sdk` 17.11.1 → 18.7.0 and `coverlet.collector` 6.0.2 → 10.0.1, superseding two Dependabot pull requests that each touched only one project and committed CRLF blobs against the repository's normalisation. A `test-platform` Dependabot group now keeps them moving together, as `microsoft-extensions` already does. `actions/setup-dotnet` v5 → v6 and `dotnet-reportgenerator-globaltool` 5.4.4 → 5.5.11 merged as-is.
- The two view-model pin branches that had no coverage at all — including `PinResult.Error`, the one every user hit — plus an AUMID write/read round trip and the new exit code in the mapping theory.
- `docs/release-process.md` now requires running pin mode directly before a release. The suite is headless and cannot reach WinRT, so no amount of unit testing would have caught a bad interop cast; running it against Windows once would have.

## [0.4.7] - 2026-09-01

Legibility release. Both defects were dark-mode only and invisible to the test suite, because nothing in it renders a window.

### Fixed

- **Dark mode is readable.** Several pieces of text were effectively invisible: measured against their actual surfaces, the sidebar group name sat at 1.37:1, the "N app(s)" subtitle at 1.26:1, the app display name at 1.27:1 and the app path at 1.16:1. The cause was dependency-property precedence rather than the `Opacity` values it resembled — `MainWindow` sets `Foreground` on the `Window`, which propagates by inheritance, but WPF's built-in theme styles set it explicitly on `ListBox`, `TextBox`, `Button`, `ComboBox` and `CheckBox`, and a theme-style setter outranks inheritance. A new `Themes/Controls.xaml` carries implicit styles for those controls, which is what structurally fixes it; the de-emphasised texts now use the `ForegroundSubtle` brush that already existed in both dictionaries and was referenced nowhere. Every body text now clears 4.5:1 in both themes.
- **The Settings window follows the theme.** It set no `Background`, no `Foreground` and no `DynamicResource` at all, so it rendered fully light regardless of the setting.
- **The title bar follows the theme.** `DWMWA_USE_IMMERSIVE_DARK_MODE` was never set, so a Mica window wore a light caption above a dark client area. Both windows now track a new `ThemeChanged` event on `IThemeService`, so a live Windows theme switch reaches the caption too.
- **Popup labels stay legible on any wallpaper.** The popup is fully transparent, so the app names render straight onto the desktop — while their colour came from the Windows app theme, which says nothing about what is behind the window. Light theme on a dark wallpaper drew near-black on near-black. The labels now carry a theme-aware halo in the opposite tone and are semi-bold; at 11 px the stroke weight mattered as much as the colour. The launch-failure strip, which forced the theme foreground onto a fixed dark red plate, got its own colour for the same reason.

### Added

- **German user manual** — `docs/benutzerhandbuch.md`, written for someone who has just installed the program, alongside the English guide. Both are linked from each other and from the README.

### Internal

- **Documentation now moves with the code.** CLAUDE.md, CONTRIBUTING.md and the pull request template all carry the same rule and the same table mapping a change to the documents it obliges. User-visible changes require both language versions of the user documentation.
- New tests parse `Controls.xaml` on an STA thread and instantiate every control template — a malformed template is not a build error and would otherwise surface only as a window that refuses to open — plus key-parity tests across the Light/Dark dictionaries in both applications.

## [0.4.6] - 2026-09-01

Patch release. Same-day follow-up to v0.4.5, from a field report on a fresh install.

### Fixed

- **"+ Add" now explains why it is disabled.** Creating a group requires a name, and the button correctly stays disabled until one is typed - but nothing on screen said so, and a fresh installer walked away unable to create a group at all. v0.4.4 had attached a tooltip explaining exactly this, which was the right instinct, except that WPF does not show tooltips on disabled controls unless `ToolTipService.ShowOnDisabled` is set. The one affordance that explained the state was unreachable in precisely that state. It is set now, and the text says what to do rather than only what the button is for.
- **The Manager opens with the cursor already in the group-name box.** Declarative `FocusManager.FocusedElement`, so the first keystroke lands where it is needed instead of requiring the field to be found first. Verified through the automation tree: on open the focused element is the name box with keyboard focus, and the disabled button exposes its help text.

### Internal

- Added the missing test coverage for the half of the v0.4.4 behaviour that matters to a user: that `AddGroupCommand` raises `CanExecuteChanged` and becomes executable as soon as a name is entered. The existing tests called `AddGroupAsync` directly and bypassed `CanExecute` entirely, so broken command wiring - a permanently dead button - would not have failed a single test.

## [0.4.5] - 2026-09-01

Patch release. Two Manager defects found while auditing the shipped behaviour for a documentation pass, plus the documentation overhaul that audit produced.

### Fixed

- **The settings dialog no longer overwrites your settings.** Opening Settings resolved one `SettingsViewModel`, loaded it, and then resolved `SettingsWindow` - which took a second, unloaded instance through its constructor, because both were registered transient. The dialog therefore always showed constructor defaults, and clicking Save wrote those defaults over `settings.json` and switched the autostart registry entry off. Two clicks, silent data loss. The view model is now a singleton, guarded by a composition-root test.
- **`dotnet run` on the Manager can find the launcher again.** The development-layout probe derived its target framework folder from the Manager's own bin path (`net8.0-windows`), but the launcher builds into `net8.0-windows10.0.19041.0` for the WinRT taskbar projections, so the probe pointed at a path that cannot exist. `TryResolve` returned null and `GroupSyncService` aborted before writing any icon, shortcut or Start menu anchor - no group created from a dev run was pinnable. The framework folder is now enumerated instead of guessed. Installed and portable layouts were never affected, which is why CI stayed green.

### Documentation

- **The guides now describe the shipped software.** `docs/architecture.md`, `user-guide.md`, `developer-guide.md` and `api-reference.md` still carried a "Status: v0.2.0" banner and had drifted into being wrong rather than merely stale: button labels that do not exist, a pinning procedure removed three releases ago, two implemented interfaces marked "no implementation yet", and a publish command producing a layout the installer cannot consume. All four rewritten against the code; the per-file version banners are gone.
- **New:** `docs/troubleshooting.md` (symptom to log line to fix, plus the known limitations), `docs/release-process.md` (the five version-bump locations and the installer smoke checklist), `docs/adr/README.md` (index, conventions, template), `SUPPORT.md`, `THIRD-PARTY-NOTICES.md`, and an issue-chooser config.
- **New ADRs:** [ADR-002](docs/adr/002-per-group-lnk-aumid.md) records the shared-launcher-plus-AUMID design and why per-group executables were rejected; [ADR-003](docs/adr/003-dpi-unit-contract.md) records the device-pixel/DIP boundary contract. Both previously existed only as a changelog bullet and a note in the tooling file. ADR-001's two standalone-executable premises are corrected in place.
- README rewritten: the status banner is a status line again rather than a three-version changelog, Quick Start leads with the Pin to taskbar button, and the requirements section resolves the three-way minimum-OS contradiction between the installer, the launcher's target framework and the developer guide.
- `SECURITY.md` supported versions corrected from 0.1.x to 0.4.x, with a real reporting path and the trust boundaries. `CONTRIBUTING.md` aligned with how the repository is actually run. `CODE_OF_CONDUCT.md` no longer claims to be a document it is not.
- Added the Keep a Changelog link references this file had been missing since 0.1.0.

### Internal

- `app.manifest` version bumped from `0.2.0.0`, where it had been stuck since v0.2.0 because it was never on the release checklist. It is on it now, in both `docs/release-process.md` and the tooling notes. Nothing reads it for versioning - the manifest carries the DPI-awareness and OS-compatibility declarations.

## [0.4.4] - 2026-07-09

Patch release, same day as v0.4.3 (field report minutes after install).

### Fixed

- **"+ Add" no longer looks dead.** Creating a group requires a name, but the command silently early-returned when the name box was empty — clicking "+ Add" without typing produced no reaction whatsoever, which reads as a broken button. The button is now disabled until a non-blank name is entered, the name box carries a "Group name…" watermark, and the button explains itself in a tooltip.

## [0.4.3] - 2026-07-09

Patch release. Root-causes and fixes the dead popup that v0.4.1 introduced and v0.4.2's defence-in-depth could not reach: clicking a pinned taskbar group did nothing except a brief busy cursor. Also makes launcher startup failures diagnosable from the log, fixes popup placement at non-100% display scaling, and repairs the CI/CodeQL pipeline.

### Fixed

- **Popup opens again.** Since v0.4.1 the popup window never came into existence: the open animation declared its `ScaleTransform` with `ScaleX/Y=0.5` defaults directly on the `Window`, and WPF's `Window.CoerceRenderTransform` rejects any non-identity transform — `InitializeComponent` threw during BAML load, the startup catch swallowed the exception into `Trace` (invisible in published builds), and the process exited with code 3 before anything was shown. The transform (and the scale animation targets) now live on the `ChromeRoot` content Border, which WPF scales freely. The storyboard is cloned before retargeting so a frozen resource instance cannot break `SetTarget`.
- **Launcher startup failures reach the log.** All launcher failure paths (missing group id, startup exception, unhandled AppDomain/dispatcher exceptions) previously reported via `Trace` only. A DI-free `StartupFailureLogger` now appends them to the same daily `launcher-*.log`, with a short retry against concurrent-writer sharing violations. Unhandled dispatcher exceptions shut down with documented exit code 4. This is why the v0.4.x regression stayed invisible for weeks — the failure mode is now structurally impossible.
- **Startup timing line is actually emitted.** The v0.4.1 timing summary subscribed to `Loaded` after `Show()`, but a `Window` raises `Loaded` synchronously inside `Show()` — no released build ever wrote the line. Subscription moved before `Show()`.
- **Popup placement is DPI-correct.** `TaskbarPositionHelper` fed raw device-pixel rects and the raw cursor position into WPF DIP coordinates — correct at 100% scaling only, ~33% drift at 150% (the documented v0.3 limit). All Win32 geometry is now converted to DIPs exactly once via `GetDpiForMonitor` on the target monitor, DPI awareness is switched on before the cursor capture so the anchor is genuinely physical, and the conversion is covered by placement tests at 150% including the off-screen right-edge-click repro.
- **Popup activation and dismissal are deterministic.** Popup mode now calls `Activate()` after `Show()` (parity with pin mode), `Deactivated` only dismisses after the window has genuinely held activation, and a 3 s fallback closes a never-activated popup (re-arming while the pointer is over it) so it cannot linger as an orphaned always-on-top window.
- **CodeQL runs again.** Two independent breaks: the workflow's explicit `permissions` block listed only `security-events: write`, zeroing `contents` — checkout of the private repository failed with "repository not found" on every scheduled run since 2026-05-25 — and the code-scanning upload requires GitHub Advanced Security, which is unavailable for private repos on the current plan. Fixed the permissions (+ `build-mode: manual` to match the explicit build step) and switched to `upload: never` with an in-workflow findings report: any CodeQL result now fails the job and is annotated inline, with the SARIF preserved as a 7-day artifact.
- **CI artifact uploads no longer hit the storage quota.** Coverage artifacts expire after 7 days, build output after 3, dependabot PRs skip the build-output upload entirely (two full Release bin trees per run at the 90-day default retention had exhausted the account quota), and upload steps are `continue-on-error` so a quota blip can never fail an otherwise-green build again.

### Internal

- Dependabot now updates the `Microsoft.Extensions.*` family as a single group, ignores its major bumps (net8.0 targets stay on the 8.x train; a solo 10.x bump produced NU1605 downgrade conflicts), and ignores `FluentAssertions` ≥ 8 (commercial license change).
- CLAUDE.md records the new invariants: launcher failure paths must reach the file log, `Window` accepts only identity `RenderTransform`s, `Window.Loaded` fires inside `Show()`, and the device-pixel/DIP unit contract at the Win32 boundary.

## [0.4.2] - 2026-05-18

Patch release. v0.4.1's pin + animation fixes both regressed on a real-world Win11 24H2 install: the popup stayed invisible, and the pin button showed our "Pinned" notify without any Windows system dialog and never produced a tile. v0.4.2 tightens both against timing races so they work regardless of how Win11 24H2 schedules paint and indexer work.

### Fixed

- **Popup is visible within 500 ms regardless of animation outcome.** v0.4.1 fired the storyboard from a one-shot `CompositionTarget.Rendering` subscription; Win11 24H2 can skip the composition pass entirely for a fully-transparent window, so `Rendering` never fires and the popup stays at the From state (`Opacity=0`, `Scale=0.5`) forever. v0.4.2 schedules `Storyboard.Begin` via `Dispatcher.BeginInvoke(DispatcherPriority.Render, …)` instead — the dispatcher cycle runs even when the compositor is skipped — and arms a 500 ms `DispatcherTimer` safety-net that snaps to the end state if the popup is still invisible. Also resolves `ChromeRoot` via `FindName` and `Storyboard.SetTarget` for the opacity child to bypass NameScope lookup, which can silently fail when the storyboard lives in `Window.Resources`.
- **Pin to taskbar reliably surfaces the Windows system dialog.** `TaskbarManager.RequestPinCurrentAppAsync` trusts the calling AUMID and can return success cosmetically when the Shell AppsFolder index has not yet seen the matching `.lnk`, persisting nothing. `GroupSyncService` now calls `SHChangeNotify(SHCNE_CREATE, SHCNF_PATHW | SHCNF_FLUSH, …)` after each Start Menu anchor write (on Sync and on the heal-up path), and `TaskbarPinRunner` waits 300 ms before invoking the WinRT pin API. The delay is below the ~400 ms "feels instant" threshold so users don't perceive added latency.

### Added

- **Pin-time diagnostic log.** `TaskbarPinRunner` logs the Start Menu anchor directory path, its existence flag, and the list of `*.lnk` filenames found inside it immediately before calling `RequestPinCurrentAppAsync`. Future "pin still doesn't work" reports can be triaged from `%APPDATA%/TaskbarFolders/logs/launcher-*.log` without asking the user for Explorer screenshots.

### Internal

- New `IShellChangeNotifier` / `ShellChangeNotifier` in `TaskbarFolders.Core/Shortcuts/`. P/Invoke for `shell32!SHChangeNotify` lives in the existing `Core/Interop/NativeMethods.cs` table; the wrapper interface keeps the call site testable without executing native code in CI.
- `GroupSyncService` constructor now takes `IShellChangeNotifier`. DI registration added in `ManagerServiceCollectionExtensions`. `CompositionRootTests` resolves the new service; `GroupSyncServiceTests` gains two behavioural tests covering Sync and heal-up notify-call sites.

## [0.4.1] - 2026-05-18

Patch release. Three v0.4.0 regressions reported from real-world install on Win11 24H2:

### Fixed

- **Pin to taskbar button actually pins now.** v0.4.0 wrote the per-group `.lnk` only under `%APPDATA%/TaskbarFolders/shortcuts/<id>.lnk` — invisible to the Shell AppsFolder index. `TaskbarManager.RequestPinCurrentAppAsync` requires a Start Menu shortcut with the matching AUMID to anchor the pin; without one the call silently fails to persist even though the system dialog shows and the API returns success. v0.4.1 writes a sibling `.lnk` at `%APPDATA%/Microsoft/Windows/Start Menu/Programs/TaskbarFolders/<display-name>.lnk` on every sync. A heal-up loop runs on Manager startup so existing v0.4.0 groups also gain Start Menu anchors.
- **Open animation visible on cold launches.** v0.4.0 fired the storyboard from `OnSourceInitialized`, but WPF first paint happened 100-200 ms later — by the time the user saw frame 1, the 200 ms timeline had elapsed and the popup snapped to its end state. v0.4.1 sets XAML defaults to match the animation's From state (Border `Opacity=0`, `ScaleX/Y=0.5`) and uses a one-shot `CompositionTarget.Rendering` subscription to start `Storyboard.Begin` on the first composition frame. Also avoids the Win11 24H2 paint-skip path triggered by `AllowsTransparency=True + Window.Opacity=0` by animating the inner Border's opacity instead of the Window's.

### Added

- **Per-checkpoint startup timing log.** Launcher `popup-mode` now logs a single line on first paint with the wall-clock ms from `tStart` to each phase (`aumid`, `settings`, `di`, `theme`, `vm`, `show`, `loaded`) plus `processAge` capturing the .NET runtime cold-start cost that happens before our code runs. Read via `%APPDATA%/TaskbarFolders/logs/launcher-*.log`. Will inform the v0.4.2 / v0.5 perf work — until we know where the user's reported ~1 s per click goes, architectural changes (persistent launcher daemon, drop `PublishSingleFile`, etc.) would be speculative.

### Known limitations (deferred)

- **Group rename leaves the old Start Menu .lnk as an orphan.** The new filename gets written but the old one remains. v0.5 will add a reconciler that enumerates `StartMenuDirectory` and deletes anchors whose AUMID does not match any current group.

## [0.4.0] - 2026-05-18

Minor release. Launcher polish triggered by hands-on use of v0.3.0: the open animation felt too subtle, the popup still had measurable startup overhead, and pinning a group to the taskbar took three manual clicks through the Explorer context menu.

### Changed

- **Popup grows up out of the clicked tile.** The open storyboard now scales 0.5→1.0 over 200 ms with a `QuinticEase` curve, and the `ScaleTransform` pivot moved from top-left to the bottom-centre of the popup (`CenterX = Width/2`, `CenterY = Height`). Since the popup sits directly above the clicked tile per `TaskbarPositionHelper`, the pivot-at-bottom-centre produces a "grow up out of the tile" feel instead of a fade-in-place. The animation now fires from `OnSourceInitialized` (before first paint) so frame 1 already shows scale=0.5 rather than a brief snap-to-1.0.
- **One-click pin to taskbar.** The new **Pin to taskbar** button in the Manager spawns the Launcher in `--pin-mode`, which calls `Windows.UI.Shell.TaskbarManager.RequestPinCurrentAppAsync()`. Windows shows its native "Allow [App] to pin?" dialog; clicking Allow pins the group with its distinct AUMID. The previous **Show shortcut...** behaviour remains as a secondary button for users on Windows builds where TaskbarManager is unsupported (LTSC, Education) — the Manager auto-falls-back to the Explorer flow in that case.

### Performance

- **Deferred startup IO.** `FileSystemIconCache.PruneStaleEntries` and `FileLoggerProvider.PruneOldFiles` moved out of their constructors into `StartBackgroundPrune()` methods that App.OnStartup fires post-`Show()`. Saves ~15-70 ms on the first-paint critical path depending on cache + log retention state.
- **Explicit popup dimensions.** `PopupWindow.SizeToContent="WidthAndHeight"` replaced with an explicit `Width = cols * 96 + 24, Height = rows * 96 + 24` computation in `OnSourceInitialized`. Skips the WPF measure pass that used to add ~5-10 ms before placement could be computed.
- **DI graph validation only in Debug.** `ServiceProvider` is built with `ValidateOnBuild=true / ValidateScopes=true` in Debug builds only. Release skips the eager-walk (~20-30 ms). `CompositionRootTests` exercise the validation explicitly so the safety net is preserved in CI.

### Internal

- `Windows.UI.Shell.TaskbarManager` requires WinRT projections, so `TaskbarFolders.Launcher.csproj` TFM bumped from `net8.0-windows` to `net8.0-windows10.0.19041.0` (Win10 1903+). Launcher.Tests TFM matches; Manager + Shared stay on `net8.0-windows`.
- `TaskbarPinRunner` attaches the WinRT manager to a foreground HWND via `WinRT.Interop.InitializeWithWindow.Initialize` so the system pin dialog parents correctly on multi-monitor / multi-foreground setups.
- `PinHostWindow` (1×1, fully transparent, centred on the primary screen) is the foreground HWND the WinRT pin dialog attaches to. Off-screen positioning broke foreground promotion on Win11 24H2 — centred + `Opacity=0` is the working compromise.
- `GroupAumid.TryExtractGroupId` reverses `GroupAumid.For` so the Launcher can recover its group id from the AUMID Windows assigned to the process. Used when Windows launches a TaskbarManager-pinned tile without preserving the original `--group-id` command line. Case-insensitive prefix match mirrors Windows' own wcsicmp-based AUMID comparison.
- `App.OnStartup` split into `RunPinModeAsync` + `RunPopupModeAsync` branches with a shared `ResolveGroupId` helper. Both branches wrapped in try/catch so any unhandled async-void exception produces a documented exit code (`Shutdown(3)`) instead of a random WPF crash code.
- `LauncherProcessPinService` (Manager) spawns the Launcher via the new `IProcessRunner` abstraction (testable Process.Start wrapper) with a 2-minute timeout, maps the exit code (0=Success, 1=UserDenied, 2=Unsupported, 3=Error) to a `PinResult` enum. View model receives the result and routes Notify accordingly.
- `IIconCache.StartBackgroundPrune` is a C# 8 default interface method (no-op default) so existing mocks continue to work.

## [0.3.0] - 2026-05-17

Minor release. Launcher popup polish triggered by hands-on use of v0.2.1: the popup opened slowly, the chrome was too visible, and placement was anchored on the taskbar centre rather than the clicked tile.

### Changed

- **Popup opens instantly.** `PopupViewModel.LoadAsync` is now metadata-only — reads the group config and populates the tile collection with empty icons in ~5 ms. The window paints immediately; a new `StartIconLoad` fires per-app icon extraction on the thread pool and assigns each `Icon` as it resolves. Pre-v0.3 the launcher froze the UI thread 200 ms-3 s on cold cache before the first paint.
- **Popup chrome is fully transparent.** The semi-opaque Border, the rounded corners, the drop shadow, and the Win11 Acrylic backdrop are all gone. Only the icons and the per-tile hover highlight are visible — the popup feels like floating icons rather than a card. Error states (missing group, launch failure) get their own per-element backdrop so the text stays readable on any wallpaper.
- **Popup is anchored on the clicked tile.** `App.OnStartup` now captures `GetCursorPos` as its very first instruction (before WPF bootstrap can let the cursor drift) and seeds the new `ICursorAnchor` singleton. `TaskbarPositionHelper.CalculatePlacement` centres the popup horizontally on the cursor X (top/bottom taskbar) or vertically on the cursor Y (side taskbar), still clamped to the monitor work area. Pre-v0.3 placement used the taskbar geometric centre regardless of tile position, which felt "random" for any tile not in the middle.

### Performance

- Settings JSON is now loaded exactly once per launcher startup (v0.2 loaded it twice — once in `App.OnStartup` for theme, once again inside `PopupWindow.PositionAndConfigureAsync`).
- `PublishReadyToRun=true` enabled for both Launcher and Manager. Ahead-of-time native compile saves ~100-200 ms of first-launch tiered-JIT warm-up; ZIP grows ~10-20 MB total which is acceptable for the perf gain on the per-click launcher critical path.

### Known limitations

- **DPI scaling** — `GetCursorPos` returns physical pixels in system-DPI space while WPF positions in DIPs. On 100% scaling these match; on 150%+ scaling the popup may be horizontally off by up to ~33% of the popup width. v0.2 already had the same bug for the taskbar rect, so v0.3 does not regress baseline behaviour. Per-monitor DPI scaling is planned for v0.3.1.

### Internal

- New `ICursorAnchor` / `LauncherCursorAnchor` (singleton, throws if `Position` read before `Seed`, last-write-wins on double-Seed).
- `TaskbarPositionHelper.CalculatePlacement` static signature gained a `Point clickAnchor` parameter — binary-incompatible but no external consumers.
- `PopupWindow` constructor now takes `AppSettings` instead of `IAppSettingsStore`.
- `PopupViewModel` implements `IDisposable` (CA1001) — disposes the icon-load `CancellationTokenSource`.
- 14 new tests: 4 `PopupViewModelTests` for the split (LoadAsync no-extractor contract, StartIconLoad parallel extraction, cache-hit fast path, cancellation), 6 `TaskbarPositionHelperAnchorTests` covering cursor-anchored placement + edge clamping, 3 `CursorAnchorTests` for the contract, 1 added `CompositionRootTests` registration check for `ICursorAnchor`. Existing `TaskbarPositionHelperTests` (10 cases) reworked to thread the new `clickAnchor` parameter through.

## [0.2.1] - 2026-05-17

Patch release. The "Show shortcut..." button was a silent no-op for every installer and portable-ZIP user of v0.2.0.

### Fixed

- **Show shortcut button now works in installed builds.** `LauncherPathResolver` only checked for `TaskbarFolders.Launcher.exe` as a side-by-side neighbour of `TaskbarFolders.Manager.exe`. The Inno Setup installer (and the release `Compress-Archive` of `./publish/*`) places them in sibling folders (`{app}\Manager\` and `{app}\Launcher\`), so resolution returned null on every shipped build, `GroupSyncService` silently skipped shortcut generation, and the pin-helper command did nothing when clicked. Added a sibling-folder probe between the existing side-by-side and dev-layout strategies.
- **Pin-helper now surfaces missing-shortcut conditions.** When the `.lnk` does not exist, the command runs a one-shot `SyncAsync` (covers the case where an earlier sync was skipped due to a now-fixed environment), then opens a dialog naming the log location instead of returning silently. Exceptions from the inline sync are caught and routed through the same dialog so they cannot escape as unhandled `AsyncRelayCommand` failures.
- `GroupSyncService` log level for unresolved launcher bumped from Warning to Error — it is a user-blocking condition, not a soft warning. `LauncherPathResolver` itself now logs the full probed-paths list so support logs pinpoint which assumption broke.

### Internal

- `IUserConfirmation` gained `Notify(caption, message)` for one-button information dialogs (backed by `MessageBox` with `OK` + `Information` icon).
- `LauncherPathResolver` exposes `internal TryResolveFrom(string baseDirectory)` so the probe sequence is exercised against fixture directories rather than `AppContext.BaseDirectory`. `InternalsVisibleTo TaskbarFolders.Manager.Tests` added to the Manager csproj.
- 5 new resolver tests (installer-layout regression, side-by-side preference, no-match contract, blank-arg rejection, contract from v0.2.0) and 4 new view-model tests for the pin-helper happy path, sync-cannot-recover path, no-binding no-op, and SyncAsync-throws path.

## [0.2.0] - 2026-05-17

First functional release. Everything described in the README is implemented and tested.

### Added

#### Manager
- Sidebar group list with **+ Add**, alphabetical sort, right-click **Delete group** (with unpin warning), inline rename via the group editor
- Group editor: drag & drop `.exe`/`.lnk` from Explorer, **Add app...** file picker, per-app **Remove**, live 256×256 composite-icon preview with 300 ms debounce
- Settings dialog with theme (System/Light/Dark), popup position (Auto/Above/Below), animations toggle, **Start with Windows** (per-user HKCU\Run entry — no elevation)
- Pin-to-taskbar helper that opens Explorer with the group's `.lnk` pre-selected
- Mica backdrop on Windows 11 22H2+; themed solid background on older Windows
- Live theme switching when Windows app theme changes (via `SystemEvents.UserPreferenceChanged`)

#### Launcher
- Acrylic popup on Windows 11 22H2+ with rounded corners + drop shadow
- Configurable grid columns (1–6) bound per group; tile hover highlight; click launches via `Process.Start` with `UseShellExecute`
- 150 ms fade + scale open animation (respects the global animations toggle)
- Click-outside-to-close (`Deactivated` event)
- Inline error banner when a launch fails (popup stays open for retry)
- Multi-monitor placement via `SHAppBarMessage`/`MonitorFromPoint`/`GetMonitorInfo`; handles bottom/top/left/right taskbars and secondary monitors with negative X
- Per-monitor V2 DPI awareness via `SetProcessDpiAwarenessContext`

#### Pinning architecture
- `IShortcutGenerator` writes a `.lnk` per group via `IShellLinkW` + `IPersistFile` + `IPropertyStore` (atomic `.tmp` + `File.Move`); stamps `PKEY_AppUserModel_ID` so each pinned tile has a distinct identity even though they all target the single signed `Launcher.exe`
- Launcher calls `SetCurrentProcessExplicitAppUserModelID` early in startup so the running process joins its pinned tile
- `GroupAumid` helper is the single source of truth for the AUMID format; both sides consume it
- Per-group artifact sync: every save regenerates the composite `.ico` and refreshes the `.lnk`

#### Core / Shared infrastructure
- `IGroupConfigStore` + `JsonGroupConfigStore` for per-group JSON persistence in `%APPDATA%/TaskbarFolders/groups/`; atomic writes via `.tmp` + `File.Move`
- `IAppSettingsStore` + `JsonAppSettingsStore` for global settings
- `IAppDataPathProvider` centralises every `%APPDATA%` path; group ids validated against `^[A-Za-z0-9._-]{1,96}$` so a hand-edited config cannot escape the per-app data root
- `ThemePreference` and `PopupPositionPreference` strongly-typed enums replace the original string preferences; values serialised in camelCase via `CamelCaseEnumConverter<T>`; `GroupConfig.Columns` clamps to `[1..6]` on assignment
- `IIconExtractor` (`ShellIconExtractor`) extracts icons from `.exe`/`.lnk`/`.ico` via `SHGetFileInfo` + `IImageList`; resolves `.lnk` targets via `IShellLinkW`
- `ICompositeIconGenerator` (`CompositeIconGenerator`) produces 1/2/3/4-tile iOS-style composites
- `IIcoFileWriter` (`IcoFileWriter`) writes multi-resolution PNG-in-ICO files (16/32/48/256)
- `IIconCache` (`FileSystemIconCache`) caches extracted icons by source-path + last-write-time hash; prunes entries older than 30 days
- `Microsoft.Extensions.Logging` rotating file sink under `%APPDATA%/TaskbarFolders/logs/`; retention default 14 days
- `System.Text.Json` source-generator context for trim/AOT-readiness
- `WindowBackdrop` helper wraps `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` for both Mica and Acrylic
- `app.manifest` for Manager declaring `PerMonitorV2` DPI awareness

#### Testing & CI
- ~170 unit tests across `TaskbarFolders.Core.Tests`, `TaskbarFolders.Manager.Tests`, `TaskbarFolders.Launcher.Tests`
- DI composition tests for both Manager and Launcher (`ValidateOnBuild` catches lifetime mismatches at provider construction)
- Multi-monitor edge tests covering negative-X secondary monitors, oversized popups, exact-fit boundaries, all preference values
- CI emits HTML coverage report via ReportGenerator + console summary in the workflow log

### Changed
- Strategy for per-group pinning chosen during the M5 spike: `.lnk` shortcuts with distinct AUMIDs targeting a single host `Launcher.exe`, rather than the originally-planned per-group native `.exe` via `BeginUpdateResource`. Eliminates the AV/Defender false-positive risk for unsigned dynamically-modified PEs in `%APPDATA%`.

### Fixed
- Release-workflow version extraction now uses PowerShell-native syntax instead of Bash parameter expansion (the workflow runs on `windows-latest` with `pwsh` as default).

## [0.1.0] - 2026-05-06

### Added

- Initial project structure with solution and all sub-projects
- Core library with icon extraction and composite icon generation interfaces
- Shared library with models (AppEntry, GroupConfig, AppSettings)
- Manager application scaffold (WPF, MVVM)
- Launcher application scaffold (WPF popup window)
- CI pipeline with build, test, and format checking
- Release pipeline with self-contained publish and Inno Setup installer
- CodeQL security analysis
- Dependabot configuration for NuGet and GitHub Actions
- Full documentation: README, Contributing Guide, Architecture, User Guide, Developer Guide
- MIT License

[Unreleased]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.5.1...HEAD
[0.5.1]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.10...v0.5.0
[0.4.10]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.9...v0.4.10
[0.4.9]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.8...v0.4.9
[0.4.8]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.7...v0.4.8
[0.4.7]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.6...v0.4.7
[0.4.6]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.5...v0.4.6
[0.4.5]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.4...v0.4.5
[0.4.4]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.3...v0.4.4
[0.4.3]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.2...v0.4.3
[0.4.2]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/gianluca-schwekendiek/taskbar-grouping/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/gianluca-schwekendiek/taskbar-grouping/releases/tag/v0.1.0
