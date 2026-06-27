# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A lightweight Windows image viewer in C# / WinForms (.NET 8, `net8.0-windows`). It is
developed on **Linux** but **only runs on Windows** — WinForms compiles cross-platform
here (via `EnableWindowsTargeting=true`) but cannot be executed or functionally tested on
Linux. There are no automated tests; verification happens by running the published app on
Windows.

## Build environment (critical)

You MUST use the **official Microsoft .NET SDK at `~/.dotnet-ms`**, not Ubuntu's
`/usr/lib/dotnet`. The distro SDK omits `Microsoft.NET.Sdk.WindowsDesktop`, so any
WinForms build fails with `MSB4019`. Every `dotnet` invocation needs:

```bash
export DOTNET_ROOT="$HOME/.dotnet-ms"
export PATH="$HOME/.dotnet-ms:$PATH"
```

`build.sh` auto-detects `~/.dotnet-ms`; raw `dotnet` commands do not.

## Common commands

```bash
# Compile / type-check (run after the export above)
dotnet build -c Debug

# Publish a Windows artifact:
./build.sh             # folder, self-contained — MOST RELIABLE, used by the installer/CI
./build.sh single      # one portable .exe; AV can block its temp self-extract (looks like "nothing happens")
./build.sh framework   # tiny exe, needs .NET 8 Desktop Runtime on target

# Cut a release (everything else is automated by CI):
git tag v1.1.0 && git push origin v1.1.0
```

A local Windows installer is built by compiling `installer/ImgViewer.iss` with Inno Setup
(Windows only); CI does this automatically.

## Architecture

Three layers: `Program.cs` (entry) → `UI/` (forms) → `Services/` (logic). UI never touches
the registry/network directly except through `Services`.

- **Image pipeline.** `Services/ImageLoader` and `ImageSaver` are the only format-aware
  code. GDI+ handles png/jpg/gif/bmp/tiff/ico; **WebP is special-cased through ImageSharp**
  (GDI+ cannot do WebP) and converted via locked `BitmapData`. Files are read fully into
  memory so the source file is never locked (enables delete/overwrite while viewing).
- **Rendering ownership.** `UI/ImageCanvas` owns the displayed `Image` and disposes the
  previous one inside `SetImage`. `MainForm` must mutate the view only through
  `canvas.SetImage(...)` — never dispose images itself. Edits (resize/crop) build a fresh
  `Bitmap` snapshot from `canvas.CurrentImage` and hand it back via `SetImage`. The canvas
  also draws the checkerboard backdrop (for PNG/WebP alpha), zoom/pan, GIF animation
  (`ImageAnimator`), and the crop overlay. The checkerboard and image are drawn to the same
  pixel-rounded rect with `WrapMode.TileFlipXY` so no checker bleeds along the edges.
- **Undo/redo** (`MainForm`) keeps two `List<Bitmap>` stacks of *independent snapshots* that
  the form owns and disposes; the canvas owns only the live image. All edits funnel through
  `ApplyEdit`, which snapshots the pre-edit image before `SetImage`. History resets whenever
  a different file is loaded.
- **Keyboard navigation** is handled in `MainForm.ProcessCmdKey` (not key events) so arrow
  keys reliably drive prev/next; `Services/FolderNavigator` enumerates siblings with a
  natural (numeric-aware) sort and wraps around. Gotcha: a menu item's `ShortcutKeys` must
  be a *valid* shortcut (modifier combo, function key, Delete or Insert) — assigning a bare
  arrow key throws `InvalidEnumArgumentException` at construction. Use `ShortcutKeyDisplayString`
  for an arrow-key hint and let `ProcessCmdKey` do the work.
- **DPI** is set in code (`Program.cs` `SetHighDpiMode`), deliberately NOT in `app.manifest`
  (doing both triggers analyzer `WFAC010`).
- **Crash visibility.** `Program.Main` installs global handlers that log to
  `%LOCALAPPDATA%\ImgViewer\crash.log` and show a dialog — a GUI app otherwise fails
  silently.

## Auto-update + release flow (how they interlock)

- `Services/Updater` queries the **GitHub Releases API** for `GitHubOwner`/`GitHubRepo`
  (`ewha-jhsim/imgviewer`), parses the `tag_name` as a version, and picks the release asset
  whose name contains `Setup` and ends in `.exe`. This requires the repo to stay **public**
  (anonymous API). An `update.url` file next to the exe overrides the API URL.
- Because the app installs under Program Files, updating is done by the **installer**, not an
  in-place swap: `ApplyAndRestart` writes a helper `.cmd` that waits for the app to exit,
  runs `Setup.exe /VERYSILENT` (UAC elevates once), and relaunches.
- `.github/workflows/release.yml` runs on `v*` tags on a Windows runner: it injects the tag
  version into both `dotnet publish -p:Version=` and `ISCC /DMyAppVersion=`, so the built
  app's assembly version and the installer filename always match — which is what makes the
  version comparison in `Updater` correct. **Releasing = pushing a tag.**

## Registry behavior

`Services/DefaultApp` writes file associations under **HKCU only** (no elevation). Windows
10+ forbids silently setting the default handler, so registration just makes the app
selectable and then opens `ms-settings:defaultapps`. `--register` / `--unregister` CLI verbs
exist for the installer to call.
