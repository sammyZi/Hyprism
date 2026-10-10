# Third-party software

Verified on **2026-10-08** against each project's GitHub repository (license file, archive status, last push)
and the winget catalog. Official sources were shallow-cloned to read their config formats; nothing from them is
compiled into Hyprism.

## How Hyprism uses these projects

Hyprism **does not bundle, link, or redistribute** any of the integrated projects. When you ask it to, it installs
the project's own official build through **winget** (or, when winget has no package, from the project's GitHub
Releases with SHA256 verification), then runs it as a **separate process** and writes its documented config files.
That keeps GPL/LGPL projects license-compatible with Hyprism's MIT license: Hyprism and each tool communicate only
through files, command lines and the tools' own CLIs.

Two pieces of Hyprism's own code run inside other apps. Both are Hyprism's work under its MIT license:

- `src/Hyprism.Modules/Assets/slideshow`: a Lively web wallpaper (HTML/JS) that animates wallpaper transitions.
- `src/Hyprism.Modules/Assets/zebar`: a Zebar widget pack. At runtime it imports Zebar's client API from esm.sh, the
  same way Zebar's own starter widgets do.

## Integrated projects (installed on demand, run as separate processes)

| Module | Project | License | Status at verification | Installed via |
|---|---|---|---|---|
| Transparent Taskbar | [TranslucentTB/TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) | GPL-3.0 | Active | winget `CharlesMilette.TranslucentTB` |
| Network & System Monitor | [zhongyang219/TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) | "Anti 996" License 1.0 (permissive, with a labor-law compliance condition) | Active | winget `zhongyang219.TrafficMonitor.Full` |
| Glass Terminal | [microsoft/terminal](https://github.com/microsoft/terminal) | MIT | Active | winget `Microsoft.WindowsTerminal` |
| Glass Terminal (prompt) | [JanDeDobbeleer/oh-my-posh](https://github.com/JanDeDobbeleer/oh-my-posh) | MIT | Active | winget `JanDeDobbeleer.OhMyPosh` |
| Glass Terminal (fonts) | [ryanoasis/nerd-fonts](https://github.com/ryanoasis/nerd-fonts) | Fonts: SIL OFL 1.1; scripts: MIT | Active | `oh-my-posh font install` (downloads Nerd Fonts releases) |
| Glass Explorer | [Maplespe/ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) | LGPL-3.0 | Not archived; **last release 2.0.1 (Feb 2024), low activity** | GitHub release `Release_x64.zip`. **No checksum is published, so Hyprism shows the SHA256 and asks before installing.** |
| Glass Everywhere | [MicaForEveryone/MicaForEveryone](https://github.com/MicaForEveryone/MicaForEveryone) | MIT | Active (2.x) | winget `MicaForEveryone.MicaForEveryone` |
| Live Wallpapers | [rocksdanister/lively](https://github.com/rocksdanister/lively) (now `lively-community/lively`) | GPL-3.0 | Active, repo moved | winget `rocksdanister.LivelyWallpaper` |
| Tiling | [glzr-io/glazewm](https://github.com/glzr-io/glazewm) | GPL-3.0 | Active | winget `glzr-io.glazewm` |
| Tiling | [LGUG2Z/komorebi](https://github.com/LGUG2Z/komorebi) | **Komorebi License 2.0: source-available, not OSI open source. Free for personal use; commercial use needs a license from the author.** Hyprism shows this in the UI. | Active | winget `LGUG2Z.komorebi` |
| Tiling (hotkeys for komorebi) | [LGUG2Z/whkd](https://github.com/LGUG2Z/whkd) | MIT | Active | winget `LGUG2Z.whkd` |
| Tiling (borders) | [lukeyou05/tacky-borders](https://github.com/lukeyou05/tacky-borders) (now `luke-you/tacky-borders`) | MIT | Active, repo moved | GitHub release, SHA256 verified against GitHub's published digest |
| Top Bar | [glzr-io/zebar](https://github.com/glzr-io/zebar) | GPL-3.0 | Active | winget `glzr-io.zebar` |
| Top Bar | [amnweb/yasb](https://github.com/amnweb/yasb) | MIT | Active | winget `AmN.yasb` |
| App Launcher | [Flow-Launcher/Flow.Launcher](https://github.com/Flow-Launcher/Flow.Launcher) | MIT | Active | winget `Flow-Launcher.Flow-Launcher` |
| Context Menu | [moudey/Shell](https://github.com/moudey/Shell) (Nilesoft Shell) | MIT | Active | winget `Nilesoft.Shell` |
| Auto Dark Mode | [AutoDarkMode/Windows-Auto-Night-Mode](https://github.com/AutoDarkMode/Windows-Auto-Night-Mode) | GPL-3.0 | Active | winget `ArminOsaj.AutoDarkMode` |
| Media & Volume Flyouts | [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout) | GPL-3.0 | Active | Microsoft Store via winget `9N45NSM4TNBP` (no winget-community package) |
| Advanced Tweaks | [ramensoftware/windhawk](https://github.com/ramensoftware/windhawk) | GPL-3.0 | Active | winget `RamenSoftware.Windhawk`. Curated mod IDs come from [ramensoftware/windhawk-mods](https://github.com/ramensoftware/windhawk-mods); each mod has its own license. |
| PowerToys | [microsoft/PowerToys](https://github.com/microsoft/PowerToys) | MIT | Active | winget `Microsoft.PowerToys` |
| Text Expander | [espanso/espanso](https://github.com/espanso/espanso) | GPL-3.0 | Active | winget `Espanso.Espanso` |
| Screenshots | [ShareX/ShareX](https://github.com/ShareX/ShareX) | GPL-3.0 | Active | winget `ShareX.ShareX` |
| Volume Mixer | [File-New-Project/EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | MIT, with three named entities excluded from the grant | Active | winget `File-New-Project.EarTrumpet` |
| Monitor Brightness | [xanderfrangos/twinkle-tray](https://github.com/xanderfrangos/twinkle-tray) | MIT | Active | winget `xanderfrangos.twinkletray` |
| Clipboard History | [sabrogden/Ditto](https://github.com/sabrogden/Ditto) | GPL-3.0 | Active | winget `Ditto.Ditto` |
| Quick Look | [QL-Win/QuickLook](https://github.com/QL-Win/QuickLook) | GPL-3.0 | Active | winget `QL-Win.QuickLook` |
| Files | [files-community/Files](https://github.com/files-community/Files) | MIT | Active | winget `FilesCommunity.Files` |

### Online wallpaper sources (Wallpapers > Discover)

Queried anonymously (no API key). Hyprism downloads only the image you pick and saves its credit line next to it.

| Source | API | Content terms |
|---|---|---|
| [Wallhaven](https://wallhaven.cc) | `wallhaven.cc/api/v1/search` (SFW only) | User-uploaded; copyright stays with each image's owner. Use for your own desktop. |
| [Openverse](https://openverse.org) | `api.openverse.org/v1/images` (Wikimedia excluded) | Creative Commons / public domain; license and creator shown on each tile. |
| [NASA Image and Video Library](https://images.nasa.gov) | `images-api.nasa.gov/search` | Generally public domain ([NASA media guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/)). |
| [Lorem Picsum](https://picsum.photos) | `picsum.photos/v2/list` | Unsplash photos under the [Unsplash License](https://unsplash.com/license). |

### Not used

- **RoundedTB** ([RoundedTB/RoundedTB](https://github.com/RoundedTB/RoundedTB), GPL-3.0) is **archived** (last push
  Oct 2023). For a rounded or floating taskbar, Hyprism points to the maintained Windhawk mod
  `windows-11-taskbar-styler` instead.

## Bundled with Hyprism

| Component | License | Notes |
|---|---|---|
| [Google Sans Flex](https://github.com/google/fonts/tree/main/ofl/googlesansflex) | SIL Open Font License 1.1 | `src/Hyprism.App/Assets/Fonts/GoogleSansFlex.ttf`; license text in `Assets/Fonts/OFL.txt`. Unmodified. |
| [Symbols Nerd Font Mono](https://github.com/ryanoasis/nerd-fonts) v3.5.1 (`NerdFontsSymbolsOnly.zip`) | MIT (Nerd Fonts; individual icon sets keep their own permissive licenses) | `src/Hyprism.App/Assets/Fonts/SymbolsNerdFontMono-Regular.ttf`; license text in `Assets/Fonts/NerdFontsSymbols-LICENSE.txt`. Unmodified; used for icons in the prompt preview. |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) | MIT | NuGet `Microsoft.WindowsAppSDK` (self-contained runtime) |
| [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) | MIT | NuGet `CommunityToolkit.WinUI.Controls.SettingsControls` |
| [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | MIT | NuGet `H.NotifyIcon.WinUI` (tray icon) |
