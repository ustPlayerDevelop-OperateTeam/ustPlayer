# AGENTS.md

Chinese-first, Windows-only PySide6 + PySide6-Fluent-Widgets desktop app that visualizes UTAU/UST project files. Comments, docstrings, log messages, and UI strings are all in Chinese (see CONTRIBUTING.md, which has the full contribution rules).

## Commands

- Setup: `uv sync` (uv; Python 3.13.12 per `.python-version`, requires >=3.11)
- Run: `uv run main.py` — the only real entry point (thin shell → `ustplayer.app.main`)
- No tests, linter, or typecheck config exists; verification is manual (launch app, load a .ust, play).
- Windows only: `winreg` is used by `ustplayer/ui/main_window.py`. It will not run on WSL/Linux.

## Gotchas

- `uv run ustplayer` also works now: `[project.scripts] ustplayer` points to `ustplayer.app:main`. Both entry paths share `AppContext`.
- USTX (`.ustx`) is **not** supported yet — the parser handles `.ust` text only. Don't claim USTX support or route `.ustx` into `UstFileReader`.
- Build/release happens only via GitHub Actions (`.github/workflows/build.yml`, Nuitka standalone on windows-latest). Commit messages starting with `pass` skip CI; messages starting with `v` trigger an auto-release. Never use a `v` prefix unless you intend to release.
- CI extracts release notes from `UPDATELOG.md` sections headed `# v{version}` — keep that exact heading format when adding entries. Top-level `## Unreleased` sections are ignored by the extractor.

## Architecture

- `main.py` — thin shell; real entry `src/ustplayer/app.py`: QApplication + `AppContext` + `MainWindow`.
- `src/ustplayer/context.py` — `AppContext`: the single composition root / facade. UI pages receive it by constructor injection and call `ctx.settings`, `ctx.parser`, `ctx.player`, `ctx.project_io`; they must not import core implementations directly.
- `src/ustplayer/core/contracts.py` — data contracts (`UstInfo`/`NoteInfo`/`PlayerLaunchParams`), service Protocols (`UstParser`/`PlayerLauncher`/`ProjectIO`), color & bool utils, app version constants.
- `src/ustplayer/core/`:
  - `log.py` — loguru, writes `ustPlayer.log` next to the exe (falls back to `%LOCALAPPDATA%\ustPlayer`) + stdout (guards `sys.stdout is None` for packaged GUI)
  - `settings_manager.py` — `SettingsManager`: thin facade that assembles the settings sub-domains in `core/settings/` and orchestrates settings read/write / validation / player-params assembly. UI accesses settings via `ctx.settings.<subdomain>.<prop>` (e.g. `ctx.settings.display.show_bpm`).
  - `settings/` — one signal-driven sub-domain per settings group (formerly ini sections, keys preserved in `Settings.json`): `project.py` (`ProjectSettings`, [ProjectSettings]), `file.py` (`FileSettings`, [FileSettings]), `display.py` (`DisplaySettings`, [DisplaySettings]), `color.py` (`ColorSettings`, [ColorSettings]), `player.py` (`PlayerSettings`, [PlayerSettings]+[LyricSettings]), `theme.py` (`ThemeSettings`, [ThemeSettings], not exported to uplr). Each class owns its properties + `Signal`s + `read_from`/`write_to`/`validate`.
  - `settings_store.py` — `SettingsStore`: `Settings.json` file I/O (group→kv dict; path resolution with read-only fallback to `%LOCALAPPDATA%\ustPlayer`), auto-migrates legacy `Settings.ini` on first run, no business logic.
  - `uplr_io.py` — `UplrProjectIO` implements `contracts.ProjectIO`: `.uplr` import/export. **New format = ZIP container** (`Info.json` + ust/lrc/music resources, extracted to `%LOCALAPPDATA%\ustPlayer\projects\<stem>-<hash8>\` on import); **old text format still imports** (auto-detected by ZIP magic). Depends on `SettingsManager` for property reads/writes; importing triggers settings signals so UI syncs live.
  - `ustreader.py` — `UstFileReader` implements `UstParser`; `.ust` text only (parses Lyric/Length/NoteNum/Phoneme/PitchBend), takes an `encoding` arg (default "Shift-JIS"); wrong encoding raises `UnicodeDecodeError`
  - `player.py` — `NotePlayerLauncher` implements `PlayerLauncher`; `NoteLyricDisplay` is the fullscreen QPainter player. Plays accompaniment (`music_path`) via QtMultimedia with position-driven timeline; falls back to wall-clock timing when no/failed audio. Renders lyric / note name / pitch-bend curve / LRC lyrics per `ShowConfig`.
- `src/ustplayer/ui/` — one page per sidebar item: basic, file, player_style, lyric, other, plus `main_window.py` (FluentWindow with sidebar navigation). Each page implements `sync_all_from_settings()`.
- `tools/uplr_converter/` — standalone C++17 converter (old text .uplr → new ZIP .uplr), zero third-party deps; built by CI on windows-latest and shipped with releases.
- `Settings.ini` / `Settings.json` are gitignored (user-local).
- `uPlRender/` is a separate nested git repo (external render module) — not part of this repo; do not edit it here.

## Conventions

- Use `from ustplayer.core.log import logger` (loguru), never `print`.
- User-facing errors follow `InfoBar.error("ERcodeXXX", "提示文案", ...)`; register any new code in `ERcode.txt` (001–009, 999 are taken).
- Large files separate sections with `# ===================== 段落名 =====================`.
- User-visible changes get a bullet in the latest `UPDATELOG.md` section.
