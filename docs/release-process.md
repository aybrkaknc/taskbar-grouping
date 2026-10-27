# Release Process

Releases are cut from `develop` by pushing a `v*` tag. Everything after that is automated except the verification that matters most — actually installing the result.

## Version numbers live in five places

They must all agree before the tag is pushed. Nothing checks this for you.

| File | What to change |
|---|---|
| `Directory.Build.props` | `<Version>` — feeds every assembly |
| `installer/setup.iss` | `#define MyAppVersion` — shown in the wizard and in Add or Remove Programs |
| `src/TaskbarFolders.Manager/app.manifest` | `assemblyIdentity version` — four-part form, e.g. `0.4.5.0` |
| `README.md` | the status line under the badges |
| `CHANGELOG.md` | a new section for the version, dated |

`app.manifest` was stuck at `0.2.0.0` from v0.2.0 until v0.4.5 because it was not on this list. Nothing reads it for versioning, so the drift was invisible — which is exactly why it needs to stay on the list.

## Before tagging

```bash
dotnet build TaskbarFolders.sln -c Release
```

```bash
dotnet test TaskbarFolders.sln -c Release
```

```bash
dotnet format TaskbarFolders.sln --verify-no-changes
```

All three must pass locally. Pushing red and fixing forward is not acceptable here — it makes bisecting a later regression far harder for a single maintainer.

If the machine only has a newer WindowsDesktop runtime, prefix the test command with `DOTNET_ROLL_FORWARD=LatestMajor`.

Then:

- Run the Manager and exercise whatever the release changed. Unit tests do not click buttons.
- **Exercise pin mode directly at least once**: `TaskbarFolders.Launcher.exe --pin-mode --group-id <id>`, then read `launcher-*.log`. Exit code 3 means the WinRT call threw and no pin is possible; the anchor diagnostic line must be present. Pin to taskbar was broken from v0.4.0 to v0.4.8 because nothing ever ran this path — the suite is headless and cannot reach WinRT.
- Check `%APPDATA%\TaskbarFolders\logs\manager-*.log` afterwards. A warning or error line with no visible counterpart in the UI is a bug in its own right.
- Make sure the documentation matches what is shipping. A release that changes behaviour and not the docs creates the next stale-documentation problem.

## Tagging

```bash
git checkout develop && git pull
git tag vX.Y.Z          # the version you just bumped to
git push origin vX.Y.Z
```

A tag containing a hyphen (`v0.5.0-rc1`) is published as a pre-release; anything else is a full release.

After the release is verified, fast-forward `main` to the released commit — every release tag to date is contained in both branches.

`main` requires the `build` check and linear history. A fast-forward from `develop` satisfies both: status checks attach to commits, so the run that already passed on `develop` counts for the same SHA on `main`.

## What the workflow does

Pushing the tag triggers `release.yml` on `windows-latest`:

1. Restore, build in Release, run the tests. A failing test stops the release.
2. Publish each executable **separately** into sibling folders — `./publish/Manager` and `./publish/Launcher` — self-contained, `win-x64`, single-file, with native libraries included for self-extraction.
3. Copy `installer/Uninstall.cmd` to `./publish/` and zip `./publish/*` into `TaskbarFolders-portable.zip`. The script runs `Manager\TaskbarFolders.Manager.exe --uninstall`; the installer does not ship it (its own uninstaller runs the same switch).
4. Build the installer with the Inno Setup compiler pre-installed on the runner. The step fails loudly if `ISCC.exe` is not where it expects — a runner image change is the likely cause.
5. Lift the matching `## [X.Y.Z]` section out of `CHANGELOG.md` and use it as the release body, with a link back to the full changelog. If no such section exists the step warns and falls back to GitHub's generated notes — which on this repository produce nothing but a compare link, because releases are cut from direct pushes rather than pull requests.
6. Create the GitHub release and attach both assets.

The job needs `contents: write`; that is already configured.

### Artifacts

| Asset | v0.4.10 size |
|---|---|
| `TaskbarFolders-portable.zip` | ≈ 167 MB (166,691,561 bytes) |
| `TaskbarFolders-Setup.exe` | ≈ 112 MB (112,208,154 bytes) |

Both executables are self-contained and ReadyToRun-compiled, so each carries its own copy of an ahead-of-time compiled .NET runtime. That is where the size goes. Expect roughly this scale for any release; a sudden drop usually means a publish step silently produced a framework-dependent build.

## After tagging, before announcing

The tag and the assets exist before anyone has confirmed they work. **Install the result and use it.**

- [ ] Read the release page. The body must carry the CHANGELOG section, not a bare compare link — a warning in the job log means the heading did not match.
- [ ] Install `TaskbarFolders-Setup.exe` on a machine that does not have the development tree.
- [ ] Create a group, add two or three apps, confirm the composite icon appears.
- [ ] Pin the group and confirm the tile shows up.
- [ ] Click the tile, confirm the popup opens next to it, and launch something from it.
- [ ] Exercise whatever this release changed.
- [ ] Open `%APPDATA%\TaskbarFolders\logs\` and read both logs.
- [ ] Extract `TaskbarFolders-portable.zip` somewhere else and repeat the first four steps.
- [ ] Uninstall with a group tile pinned, and confirm nothing is left: the program folder under `%LOCALAPPDATA%\Programs`, the *Installed apps* entry, the Run value, both `TaskbarFolders.Groups` keys under `HKCU\Software\Classes\{exefile,lnkfile}\shell`, `%APPDATA%\TaskbarFolders`, the Start menu folder, the desktop icon, `%TEMP%\.net\TaskbarFolders.*` and the tile must all be gone.
- [ ] Do the same with the portable copy's `Uninstall.cmd`; the extracted folder must be gone afterwards. Then run it again and answer *No*: nothing may be deleted.

Two minutes of clicking catches what the test suite cannot. v0.2.0 shipped with **Show shortcut…** broken on every real installation: the development layout and the CI runner both happened to place the launcher where the Manager could find it, and the installed layout did not. Only an installed smoke test would have caught it.

Announce the release once this passes. If you cannot run the smoke test, say so explicitly rather than implying the release was verified.

## If a release is broken

Fix forward with a patch release the same day when the defect blocks normal use. Cut `v0.x.y+1` from `develop` with the fix, the CHANGELOG entry and the version bumps in one release-eligible state.

Do not delete or move a published tag. The assets are already downloadable and the release notes already reference it.
