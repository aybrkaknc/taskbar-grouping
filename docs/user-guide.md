# User Guide

Everything TaskbarFolders can do, in the order you are likely to need it. If something is not behaving, jump to [Troubleshooting](troubleshooting.md).

> Deutschsprachige Fassung: [Benutzerhandbuch](benutzerhandbuch.md). Both documents describe the same behaviour and are maintained together — a change to one is incomplete without the other.

## Installing

### Installer

Download `TaskbarFolders-Setup.exe` from the [Releases page](https://github.com/gianluca-schwekendiek/taskbar-grouping/releases) and run it.

The installer needs administrator rights — it writes to Program Files. Two optional steps in the wizard:

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

Only `.exe` and `.lnk` are accepted; anything else in a dropped selection is ignored without an error. The display name comes from the file name, and the icon is read from the file itself — for a shortcut, from whatever it points at.

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
- **If a launch fails**, the popup stays open and shows an error strip naming the app.
- **Click anywhere else**, or press **Escape**, to dismiss the popup. A click outside works even if Windows did not give the popup the focus.

With *Enable popup animations* on, the popup fades out briefly as it closes. On Windows 11 22H2 and later the glass panel itself cannot fade, so it disappears right after the icons have.

Escape is the only keyboard shortcut. No icon is focused when the popup opens, and the arrow keys do not move between icons.

## Editing and deleting groups

Select a group to add or remove apps; every change is saved as you make it, and the icon and shortcut are regenerated.

To rename: select the group, edit the name at the top of the detail pane, and click **Rename** or press `Enter`. **Rename** stays greyed out until the name differs from the current one. The sidebar re-sorts, and the shortcut and Start menu entry follow the new name. A tile already pinned to the taskbar keeps working — it belongs to the group, not to its name.

To delete: right-click the group in the sidebar → **Delete group** → confirm. This removes the configuration, the generated icon, the shortcut and the group's Start menu entry.

**Deleting a group does not remove its taskbar tile.** Right-click the tile and choose *Unpin from taskbar* yourself; the confirmation dialog reminds you.

There is currently no way to reorder apps, set a custom icon, or give an app launch arguments from the interface.

## Settings

Open with the **⚙** button in the top right.

![Settings dialog](../assets/screenshots/settings.png)

| Setting | Options | Default | Effect |
|---|---|---|---|
| Theme | System, Light, Dark | System | *System* follows the Windows app theme and switches live when you change it. |
| Popup position | Auto, Above, Below | Auto | *Auto* places the popup on the sensible side of the taskbar for its current edge. |
| Enable popup animations | on / off | on | Turns the popup's open animation on or off. |
| Start TaskbarFolders Manager when Windows starts | on / off | off | Adds or removes the per-user Run registry entry. |

Settings apply when you click **Save**. Closing the dialog discards changes; an *Unsaved changes* marker appears while any are pending.

The autostart checkbox reflects the registry, not the settings file — if you remove the Run entry by hand, the dialog shows it as off.

The theme covers the whole interface, including the Settings dialog, the standard controls and the window title bar. Switching it takes effect immediately; there is no restart.

### Popup grid width

Each group has a `columns` value between 1 and 6, defaulting to 3, controlling how wide its popup grid is. There is no interface for it yet — edit the group's JSON file directly (see below) and reopen the popup.

## Where your files are

Everything lives under `%APPDATA%\TaskbarFolders\`. Paste that into Explorer's address bar to get there.

| Path | Contents |
|---|---|
| `groups\<id>.json` | One file per group — its name, its apps, its column count |
| `icons\<id>.ico` | The generated composite icon |
| `icons\cache\` | Cached source icons; safe to delete, they are re-extracted |
| `shortcuts\<id>.lnk` | The shortcut you pin |
| `settings.json` | Your settings |
| `logs\` | One log file per day, kept for two weeks |

Editing a group's JSON while the Manager is open is not recommended — it will overwrite your edit on the next change. Close it first.

The file name is what identifies a group. Renaming `groups\abc.json` renames the group's identity, which orphans its icon, shortcut and any existing pin.

## Uninstalling

**Installer:** *Settings → Apps → Installed apps → TaskbarFolders → Uninstall*, or *Add or Remove Programs*. This removes the program and the autostart entry.

**Portable:** delete the extracted folder.

Neither removes your groups. To clear everything:

1. Unpin any group tiles from the taskbar.
2. Delete `%APPDATA%\TaskbarFolders`.
3. Delete `%APPDATA%\Microsoft\Windows\Start Menu\Programs\TaskbarFolders`, which holds one Start menu entry per group.
