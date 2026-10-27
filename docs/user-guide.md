# User Guide

Everything TaskbarFolders can do, in the order you are likely to need it. If something is not behaving, jump to [Troubleshooting](troubleshooting.md).

> Deutschsprachige Fassung: [Benutzerhandbuch](benutzerhandbuch.md). Both documents describe the same behaviour and are maintained together — a change to one is incomplete without the other.

## Installing

### Installer

Download `TaskbarFolders-Setup.exe` from the [Releases page](https://github.com/gianluca-schwekendiek/taskbar-grouping/releases) and run it.

The installer installs for your user account only, into `%LOCALAPPDATA%\Programs\TaskbarFolders`, and does not ask for administrator rights. Two optional steps in the wizard:

- **Start with Windows** — pre-selected. It adds a per-user entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Clear the checkbox if you would rather start the Manager yourself; you can also change it later in Settings.
- **Create a desktop icon** — not selected by default.

The wizard is available in English and German. The application itself is English only.

### Portable

Download `TaskbarFolders-portable.zip` and extract it anywhere. Run `Manager\TaskbarFolders.Manager.exe`.

Keep the `Manager` and `Launcher` folders next to each other. The Manager locates the launcher relative to its own folder; separating them breaks group creation.

Neither package needs a .NET runtime installed — both executables are self-contained.

## Creating a group

1. Type a name into the box at the top of the sidebar.
2. Click **+ Add**, or press Enter.

**+ Add** stays disabled until the box contains something other than blank space, and the box shows a *Group name…* watermark while empty. Groups are listed alphabetically, with the number of apps under each name.

## Adding apps

![The Manager with a group selected](../assets/screenshots/manager-dark.png)

With a group selected, either:

- **drag** `.exe` or `.lnk` files onto the app list from Explorer, or
- click **Add app…** and pick one or more files.

Only `.exe` and `.lnk` are accepted; anything else in a dropped selection is ignored without an error. The display name comes from the file name, and the icon is read from the file itself — for a shortcut, from whatever it points at. A program that has no icon of its own — or whose file can no longer be found — shows a plain window outline instead, in the list, in the popup and in the group's icon.

The path under each name is shortened for display: anything inside your user folder shows as `%USERPROFILE%\…` so your account name is not on screen in a screenshot or a screen share. Hover the path to see it in full. What is stored and launched is always the real path.

The composite preview refreshes shortly after you stop making changes.

### How the composite icon is built

Only the **first four** apps in the group contribute to the icon. The popup still shows all of them.

| Apps | Layout |
|---|---|
| 1 | Single icon, centred |
| 2 | Side by side |
| 3 | iOS-style: two on top, one below |
| 4 or more | The first four in a 2×2 grid |

### Using your own image as the icon

Click **Change icon…** under the preview and pick an `.ico`, `.png`, `.jpg` or `.bmp`. The group's icon — its shortcut, Start menu entry and pinned tile — then shows that image instead of its apps, whatever apps you add or remove. A copy is stored, fitted into a square with transparent edges, so moving or deleting the original file changes nothing. For an `.ico` the largest size it contains is used.

**Use app icons** goes back to the icon built from the apps. If a pinned tile keeps the old picture, Windows has not refreshed its icon yet; signing out and in again, or restarting Explorer, makes it re-read.

### Removing an app

Click **Remove** on its row. The group is saved and its icon regenerated immediately.

## Pinning a group to the taskbar

A group needs at least one app before it can be pinned — an empty group produces no icon and no shortcut.

### The direct way

Click **Pin to taskbar**. Windows shows its own confirmation dialog; approve it and the tile appears.

That dialog comes from Windows and cannot be skipped or automated. If you dismiss it, nothing is pinned and the Manager stays quiet.

The Manager then checks that a tile really appeared rather than taking Windows' word for it. In the rare case where the pin is reported but nothing shows up, you get **Pin not confirmed** and the shortcut folder opens so you can pin by hand — you will never be told a group is pinned when it is not.

### If direct pinning is unavailable

Some Windows editions and managed environments block programmatic pinning, and it needs Windows 10 version 2004 or newer. When it is unavailable, the Manager says so and opens the folder containing the group's shortcut.

From there: right-click the `.lnk` → **Show more options** (Windows 11 22H2 and later) → **Pin to taskbar**.

You can open that folder yourself at any time with **Show shortcut…**.

## Using a group

Click the pinned tile. A popup opens next to the taskbar, anchored near the tile you clicked, showing every app in the group. On Windows 11 22H2 and later it sits on a blurred, translucent glass panel in your theme's tone, like Windows' own flyouts. Older versions get a plain translucent panel.

![The popup](../assets/screenshots/popup.png)

- The group's name is shown at the top of the popup.
- **Click an icon** to launch it. The popup closes.
- **Click ✕** in the top-right corner to close the popup without launching anything.
- **Click +** to add apps: a file picker opens, and every .exe or .lnk you choose is added to the group and appears in the popup. The taskbar icon and shortcut are updated as they are from the Manager. The popup stays open while the picker is.
- **Click ✎** to remove apps: each tile gets a small ✕, and clicking it takes that app out of the group — only out of the group; the program or shortcut on disk is not touched. Tiles do not launch while editing. Click the ✓ (or press **Escape**) when done. Removing the last app leaves an empty group, which you can fill again with +.
- **If a launch fails**, the popup stays open and shows an error strip naming the app.
- **Click the pin** to keep the popup open: while it is highlighted, clicking elsewhere, switching windows and launching an app no longer close it, so you can start several apps in a row. ✕, **Escape** or a click on the group's taskbar tile still close it, and the next popup opens unpinned.
- **Click anywhere else**, or press **Escape**, to dismiss the popup. A click outside works even if Windows did not give the popup the focus.
- **Click the group's taskbar tile again** to close the popup; it does not reopen.

With *Enable popup animations* on, the popup fades out briefly as it closes. On Windows 11 22H2 and later the glass panel itself cannot fade, so it disappears right after the icons have.

Escape is the only keyboard shortcut; in edit mode the first press leaves edit mode. No icon is focused when the popup opens, and the arrow keys do not move between icons.

### Sending files from Explorer

Right-click an `.exe` or a shortcut in Explorer or on the desktop → **Show more options** (or **Shift+F10**) → **Taskbar Groups** → the group. The file is added and the group's popup opens showing it; select several files to add them all at once. Windows 11 lists the entry in the classic menu only — its compact first menu takes only packaged apps. Turn the entry off in Settings.

## Editing and deleting groups

Select a group to add or remove apps; every change is saved as you make it, and the icon and shortcut are regenerated.

To rename: select the group, edit the name at the top of the detail pane, and click **Rename** or press `Enter`. **Rename** stays greyed out until the name differs from the current one. The sidebar re-sorts, and the shortcut and Start menu entry follow the new name. A tile already pinned to the taskbar keeps working — it belongs to the group, not to its name.

To delete: right-click the group in the sidebar → **Delete group** → confirm. This removes the configuration, the generated icon, the shortcut and the group's Start menu entry.

**Deleting a group does not remove its taskbar tile.** Right-click the tile and choose *Unpin from taskbar* yourself; the confirmation dialog reminds you.

There is currently no way to reorder apps or give an app launch arguments from the interface.

## Settings

Open with the **⚙** button in the top right.

![Settings dialog](../assets/screenshots/settings.png)

| Setting | Options | Default | Effect |
|---|---|---|---|
| Theme | System, Light, Dark | System | *System* follows the Windows app theme and switches live when you change it. |
| Popup position | Auto, Above, Below | Auto | *Auto* places the popup on the sensible side of the taskbar for its current edge. |
| Enable popup animations | on / off | on | Turns the popup's open animation on or off. |
| Start TaskbarFolders Manager when Windows starts | on / off | off | Adds or removes the per-user Run registry entry. |
| Show "Taskbar Groups" in Explorer's right-click menu | on / off | on | Lists your groups under *Show more options* for `.exe` and `.lnk` files. Kept up to date by the Manager. |

Settings apply when you click **Save**. Closing the dialog discards changes; an *Unsaved changes* marker appears while any are pending.

The autostart checkbox reflects the registry, not the settings file — if you remove the Run entry by hand, the dialog shows it as off.

**Uninstall TaskbarFolders…**, at the bottom, starts the uninstaller that came with this copy and closes the Manager — see [Uninstalling](#uninstalling).

The theme covers the whole interface, including the Settings dialog, the standard controls and the window title bar. Switching it takes effect immediately; there is no restart.

### Popup grid width

Each group has a `columns` value between 1 and 6, defaulting to 3, controlling how wide its popup grid is. A group with more rows than fit on screen — about eight — scrolls with the mouse wheel; a thin bar on the right shows where you are. There is no interface for it yet — edit the group's JSON file directly (see below) and reopen the popup.

## Where your files are

Everything lives under `%APPDATA%\TaskbarFolders\`. Paste that into Explorer's address bar to get there.

| Path | Contents |
|---|---|
| `groups\<id>.json` | One file per group — its name, its apps, its column count |
| `icons\<id>.ico` | The generated composite icon |
| `icons\cache\` | Cached source icons; safe to delete, they are re-extracted |
| `icons\custom\<id>.png` | The image you chose as a group's icon, if any |
| `shortcuts\<id>.lnk` | The shortcut you pin |
| `settings.json` | Your settings |
| `logs\` | One log file per day, kept for two weeks |

Editing a group's JSON while the Manager is open is not recommended — it will overwrite your edit on the next change. Close it first.

The file name is what identifies a group. Renaming `groups\abc.json` renames the group's identity, which orphans its icon, shortcut and any existing pin.

## Uninstalling

Uninstalling removes TaskbarFolders and everything it added to Windows, and leaves nothing behind:

- the group tiles on the taskbar (they are unpinned),
- the *Taskbar Groups* entry in Explorer's right-click menu,
- the start-with-Windows entry,
- the Start menu entries and the desktop icon,
- **your groups and settings** — all of `%APPDATA%\TaskbarFolders`,
- the runtime files .NET unpacks to `%TEMP%\.net`, and the records Windows keeps about programs it has run.

Back up `%APPDATA%\TaskbarFolders\groups` first if you want to keep your groups for a later reinstall.

**Installer:** *Settings → Apps → Installed apps → TaskbarFolders → Uninstall*, or **Uninstall TaskbarFolders…** at the bottom of the Manager's Settings.

**Portable:** run `Uninstall.cmd` in the extracted folder, or click **Uninstall TaskbarFolders…** in Settings. After you confirm, it removes everything listed above and then the `Manager` and `Launcher` folders and itself; the extracted folder goes too if nothing else is in it. Deleting the folder without this leaves the menu entry, the tiles and your data behind — if that already happened, extract the ZIP again and run its `Uninstall.cmd`.

If a tile cannot be unpinned, the uninstaller says so: right-click it and choose *Unpin from taskbar*.
