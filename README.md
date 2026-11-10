<a id="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![CI](https://github.com/aybrkaknc/taskbar-grouping/actions/workflows/ci.yml/badge.svg)](https://github.com/aybrkaknc/taskbar-grouping/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

<br />
<div align="center">
  <h3 align="center">TaskbarFolders</h3>

  <p align="center">
    macOS-style app folders for the Windows 11 taskbar. Group related apps behind a single pinned tile; click it and they fan out in a popup anchored to the tile.
    <br />
    <a href="https://github.com/aybrkaknc/taskbar-grouping"><strong>Explore the repository</strong></a>
    <br />
    <br />
    <a href="https://github.com/aybrkaknc/taskbar-grouping/issues/new?labels=bug">Report Bug</a>
    &middot;
    <a href="https://github.com/aybrkaknc/taskbar-grouping/issues/new?labels=enhancement">Request Feature</a>
  </p>

  <p align="center">
    <a href="README.md">English</a> | <a href="README.tr.md">Türkçe</a>
  </p>
</div>

> **This is a fork** of [gianluca-schwekendiek/taskbar-grouping](https://github.com/gianluca-schwekendiek/taskbar-grouping), developed independently. Work in progress lives on the [`feature/send-to-group`](https://github.com/aybrkaknc/taskbar-grouping/tree/feature/send-to-group) branch and is not released yet. It adds:
>
> - groups can be renamed, and renaming no longer leaves a stale Start menu entry (upstream #25, #28);
> - a popup header with the group name, close, add-apps, edit-mode and **pin-open** buttons, a glass background, and a fade-out;
> - **Taskbar Groups** in Explorer's right-click menu to send a program or shortcut to a group;
> - the pinned tile's icon follows the group's apps, a second click on the tile closes the popup, and large groups scroll (upstream #32);
> - a per-user installer and an uninstaller that leaves nothing behind.
>
> See the branch's [CHANGELOG](https://github.com/aybrkaknc/taskbar-grouping/blob/feature/send-to-group/CHANGELOG.md) for details. Releases and downloads below are still the upstream project's.

<details>
  <summary>Table of Contents</summary>
  <ol>
    <li><a href="#about-the-project">About The Project</a></li>
    <li><a href="#requirements">Requirements</a></li>
    <li><a href="#installation">Installation</a></li>
    <li><a href="#quick-start">Quick Start</a></li>
    <li><a href="#how-it-works">How It Works</a></li>
    <li><a href="#settings">Settings</a></li>
    <li><a href="#building-from-source">Building from Source</a></li>
    <li><a href="#documentation">Documentation</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
  </ol>
</details>

## About The Project

<p align="center">
  <img src="assets/screenshots/tbgroup.gif" alt="Clicking a pinned group tile opens the popup and launches an app" width="420">
</p>

* **Groups of apps behind one taskbar tile.** Drag `.exe` or `.lnk` files into a group; the group gets its own pinnable shortcut.
* **Composite group icons** generated from the apps inside the group.
* **One-click pinning.** The Manager asks Windows to pin the group. If your edition or policy blocks programmatic pinning, it opens the shortcut folder so you can pin it by hand.
* **A popup that looks like Windows.** Mica, Mica Alt, Acrylic or a plain panel, with an optional colour tint. It closes when it loses focus or after you launch something.
* **Follows your Windows theme**: light, dark, or system.
* **Multi-monitor and high-DPI aware.**

**Not included:** a taskbar replacement or shell extension, a launcher with search or hotkeys, or full compatibility with every Windows version (see [Requirements](#requirements)).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Requirements

| | |
|---|---|
| **OS** | Windows 11 recommended. The installer permits Windows 10 1809 and later. |
| **.NET runtime** | Not required. Releases are self-contained. |
| **Architecture** | x64. There is no ARM64 build. |

* **One-click pinning** needs Windows 10 2004 (build 19041) or later and an edition that permits programmatic pinning. Otherwise the Manager opens the shortcut folder for a manual pin.
* **The Mica backdrop** needs Windows 11 22H2 or later. Older builds get solid backgrounds.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Installation

1. Download `TaskbarFolders-Setup.exe` from [Releases](https://github.com/gianluca-schwekendiek/taskbar-grouping/releases). It installs per user into `%LOCALAPPDATA%\Programs\TaskbarFolders` and needs no administrator rights.
2. Launch **TaskbarFolders Manager** from the Start menu.

**Portable:** download `TaskbarFolders-portable.zip`, extract it, keep the `Manager` and `Launcher` folders side by side, and run `Manager\TaskbarFolders.Manager.exe`.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Quick Start

1. Open **TaskbarFolders Manager**.
2. Click **+ New group**, type a name and press `Enter`.
3. Drop `.exe` or `.lnk` files onto the **Apps** tab, or use **Add apps...**.
4. Click **Pin to taskbar**. Windows asks you to confirm.
5. Click the new taskbar tile and click any icon in the popup to launch it.

A group with no apps produces no icon and no shortcut, so add at least one app before pinning.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## How It Works

Every group gets its own `.lnk` in `%APPDATA%\TaskbarFolders\shortcuts\`. All shortcuts point to one shared `TaskbarFolders.Launcher.exe` with a `--group-id` argument and a distinct AppUserModelID (`TaskbarFolders.Group.<id>`), so Windows shows each group as an independent taskbar tile.

A matching shortcut is also written to the Start menu, because Windows only keeps a programmatic pin if the AppUserModelID is known to the Start menu index.

Details: [docs/architecture.md](docs/architecture.md) and [ADR-002](docs/adr/002-per-group-lnk-aumid.md).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Settings

| Setting | Options | Default |
|---|---|---|
| Theme | System, Light, Dark | System |
| Popup position | Auto, Above, Below | Auto |
| Popup backdrop | Mica, Mica Alt, Acrylic, Off | Mica |
| Popup tint | None, Accent, Midnight, Forest, Sunset | None |
| Enable popup animations | on / off | on |
| Start TaskbarFolders Manager when Windows starts | on / off | off |
| Show "Taskbar Groups" in Explorer's right-click menu | on / off | on |

Data is stored under `%APPDATA%\TaskbarFolders\` (groups, icons, shortcuts, settings and logs). Uninstalling removes this folder, so back up `groups\` first if you want to keep your groups. Troubleshooting: [docs/troubleshooting.md](docs/troubleshooting.md).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Building from Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```sh
git clone https://github.com/aybrkaknc/taskbar-grouping.git
cd taskbar-grouping
dotnet build TaskbarFolders.sln -c Release
dotnet test TaskbarFolders.sln -c Release
dotnet format TaskbarFolders.sln --verify-no-changes
```

The format check is a CI gate, so run it before pushing. See [docs/developer-guide.md](docs/developer-guide.md) for the full setup.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Documentation

* [User Guide](docs/user-guide.md)
* [Benutzerhandbuch](docs/benutzerhandbuch.md) (German)
* [Troubleshooting](docs/troubleshooting.md)
* [Architecture](docs/architecture.md)
* [Developer Guide](docs/developer-guide.md)
* [API Reference](docs/api-reference.md)
* [Release Process](docs/release-process.md)
* [Architecture Decision Records](docs/adr/README.md)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the branching model, commit conventions and required checks. Bug reports and feature requests go through the [issue templates](.github/ISSUE_TEMPLATE). [SUPPORT.md](SUPPORT.md) explains where to ask questions and [SECURITY.md](SECURITY.md) covers vulnerability reports.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## License

MIT. See [LICENSE](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[contributors-url]: https://github.com/aybrkaknc/taskbar-grouping/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[forks-url]: https://github.com/aybrkaknc/taskbar-grouping/network/members
[stars-shield]: https://img.shields.io/github/stars/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[stars-url]: https://github.com/aybrkaknc/taskbar-grouping/stargazers
[issues-shield]: https://img.shields.io/github/issues/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[issues-url]: https://github.com/aybrkaknc/taskbar-grouping/issues
