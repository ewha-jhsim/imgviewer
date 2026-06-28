# ImgViewer

A lightweight Windows image viewer built with C# / WinForms (.NET 8).

## Features

- **Formats:** PNG, JPG/JPEG, GIF (animated), BMP, WebP, TIFF, ICO.
  WebP is decoded/encoded with the pure-managed [ImageSharp](https://github.com/SixLabors/ImageSharp) library — no native codec required.
- **Transparency:** PNG/WebP alpha is shown over a checkerboard backdrop.
- **Folder navigation:** `←` / `→` move to the previous/next image in the same folder (natural sort, wraps around).
- **Zoom & pan:** mouse wheel to zoom at the cursor, drag to pan, double-click to toggle fit/100%.
- **Editing:** Resize (pixels or percent, with aspect lock), Crop (drag a region, Apply), and undo/redo. Save back to any supported format.
- **Printing:** Print and Print Preview (fits the page, keeps aspect ratio).
- **Default app:** register ImgViewer as an image handler and open Windows' Default Apps page (`Tools ▸ Set as default image viewer…`).
- **Auto-update:** checks a remote JSON manifest on launch and via `Help ▸ Check for updates…`; downloads and self-replaces the exe.

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| `←` / `→` (or PgUp/PgDn, Space) | Previous / next image |
| Mouse wheel | Zoom at cursor |
| Drag | Pan |
| Double-click | Toggle fit ⇄ 100% |
| `Ctrl`+`0` / `Ctrl`+`1` | Fit to window / actual size |
| `Ctrl`+`+` / `Ctrl`+`-` | Zoom in / out |
| `Ctrl`+`R` | Crop (then `Enter` to apply, `Esc` to cancel) |
| `Ctrl`+`O` / `Ctrl`+`S` / `Ctrl`+`Shift`+`S` | Open / Save / Save As |
| `Ctrl`+`P` | Print |
| `Ctrl`+`Z` / `Ctrl`+`Y` | Undo / Redo |
| `Delete` | Send current file to Recycle Bin |
| `F11` / `Esc` | Toggle / exit fullscreen |

## Building (cross-compile from Linux/macOS)

WinForms targets Windows, but the project sets `EnableWindowsTargeting=true` so it
**compiles on Linux/macOS**. You must use the **official Microsoft .NET 8 SDK** — the
Ubuntu/Debian `dotnet-sdk-8.0` package omits the required *WindowsDesktop SDK*.

```bash
# One-time: install the official SDK locally (no root needed)
wget https://dot.net/v1/dotnet-install.sh
bash dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet-ms"

# Recommended: self-contained FOLDER publish (no .NET needed on target, most reliable)
./build.sh                # -> publish/win-x64/  (copy the whole folder, or build an installer)

# One portable .exe (self-contained, single file). Convenient, but some antivirus
# setups block its temp self-extraction, making it look like "nothing happens".
./build.sh single         # -> publish/single/ImgViewer.exe

# Tiny exe that needs the .NET 8 Desktop Runtime installed on the target.
./build.sh framework      # -> publish/framework/
```

> Note: the app only **runs** on Windows. On Linux you can build and publish it, but
> functional testing must be done on a Windows machine.

## Distributing

Two ready-to-ship options, both produced from the **folder** publish:

- **Portable ZIP** — zip `publish/win-x64/` and ship it. The user extracts anywhere and
  runs `ImgViewer.exe`. No install, no .NET needed.
- **Installer** — a proper `Setup.exe` with Start Menu / desktop shortcuts and an
  uninstaller, via [Inno Setup](https://jrsoftware.org/isdl.php) (free):
  1. On Windows, install Inno Setup 6.3+.
  2. Make sure `publish/win-x64/` exists (run `./build.sh`).
  3. Open `installer/ImgViewer.iss` and click **Compile** (or run
     `ISCC.exe installer\ImgViewer.iss`).
  4. The installer appears at `dist/ImgViewer-1.0.0-Setup.exe`.

  (Inno Setup compiles only on Windows; the `.iss` script is provided ready to use.)

## Troubleshooting: "I run it and nothing happens"

- **You ran the single-file build and nothing happened:** its first-launch self-extraction
  was likely blocked by antivirus/policy. Use the **folder** build / ZIP / installer instead.
- **Any other startup failure** is now caught and written to
  `%LOCALAPPDATA%\ImgViewer\crash.log`, plus an error dialog. Check that file if the
  window doesn't appear.
- Make sure you're on **64-bit Windows** (the build targets `win-x64`).

## Setting as the default image viewer

Since Windows 10, an app cannot silently make itself the default for a file type — the
user must confirm. ImgViewer makes this as easy as possible:

1. Run `ImgViewer.exe` and choose `Tools ▸ Set as default image viewer…`
   (or run `ImgViewer.exe --register` once).
2. Windows opens the **Default Apps** settings page; pick ImgViewer for the formats you want.
3. To undo: `Tools ▸ Remove file associations` (or `ImgViewer.exe --unregister`).

Associations are written under `HKCU` (per-user), so no administrator rights are needed.

## Auto-update (via GitHub Releases)

On launch (and from `Help ▸ Check for updates…`) the app calls the GitHub API for the
**latest release**, compares its tag (e.g. `v1.1.0`) with the running build, and — if newer —
downloads the release's `Setup.exe` asset, runs it silently (one UAC prompt to upgrade the
Program Files install), and relaunches.

**One-time setup:** edit `Services/Updater.cs` and set your repo:

```csharp
public const string GitHubOwner = "your-github-username";
public const string GitHubRepo  = "imgviewer";
```

(Or override per-install without rebuilding: drop an `update.url` file next to `ImgViewer.exe`
containing a GitHub "latest release" API URL.)

### Cutting a release — just push a tag

The repo includes `.github/workflows/release.yml`. On any `v*` tag it builds the app on a
Windows runner, compiles the Inno Setup installer, and uploads it to a GitHub Release:

```bash
git tag v1.1.0
git push origin v1.1.0
```

That's the whole release process — no manual Windows build needed. Every installed app then
auto-updates to the new version on next launch. (The tag version flows into both the app's
assembly version and the installer filename, so version comparisons line up.)

## Project layout

```
ImgViewer.csproj          # net8.0-windows, WinForms, EnableWindowsTargeting
app.manifest              # longPathAware + OS compatibility (DPI is set in code)
Program.cs                # entry point, --register/--unregister verbs
Services/
  ImageLoader.cs          # load any format (WebP via ImageSharp) into a GDI+ image
  ImageSaver.cs           # save back out (WebP via ImageSharp, JPEG alpha-flatten)
  FolderNavigator.cs      # sibling enumeration + natural sort + prev/next
  DefaultApp.cs           # registry file associations / default-app registration
  Updater.cs              # GitHub Releases check, download installer, silent reinstall
UI/
  MainForm.cs             # window, menus, keyboard, drag&drop, edit/save orchestration
  ImageCanvas.cs          # checkerboard + alpha rendering, zoom/pan, GIF animation, crop overlay
  ResizeDialog.cs         # width/height with aspect lock
  UpdateProgressForm.cs   # download progress dialog
installer/ImgViewer.iss   # Inno Setup script -> Setup.exe
.github/workflows/release.yml  # tag push -> build + installer + GitHub Release
build.sh                  # cross-build helper (folder / single / framework)
```
