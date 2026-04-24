# TeamTalk Streamer

**TeamTalk Streamer** is an accessible Windows application that streams your default playback device's audio — whatever your PC is currently playing — directly into a TeamTalk voice channel. It is designed as a clean, screen-reader-first replacement for the traditional "Virtual Audio Cable plus TeamTalk" setup, with every state change spoken aloud, non-verbal feedback tones, and a keyboard-only workflow.

## What it does

- Captures your default Windows render device via WASAPI loopback.
- Connects to a TeamTalk 5 server, logs in, and joins a configured channel.
- Pushes the captured PCM into the channel via the TeamTalk SDK so other members hear exactly what you are playing.
- Handles the full lifecycle in one click — connect, log in, join channel, begin streaming — and again in one click to stop.
- Optionally excludes one running app (typically your screen reader) from the captured mix, so its speech stays on your side only. Uses Windows 10 2004+'s Process Loopback API under the hood.
- Exposes a live **Master volume** slider (0–100 %) so the stream's output level into the channel can be tuned on the fly without alt-tabbing to Windows' volume mixer.

## Status

- **Version 1 (current):** Windows streaming of the default playback device. Includes a credentials dialog with a built-in channel browser, an excluded-apps picker for screen-reader-style process exclusion, a live master-volume slider, adjustable feedback-tone volume via `=` / `-`, a single start/stop toggle button, F1 user-guide, and fully clean process shutdown. Ready for day-to-day use.
- **Version 2 (deferred):** LAN companion apps for iOS (SwiftUI + ReplayKit) and Android (Kotlin + MediaProjection) so your phone can stream its audio into the same channel through this app. The server-side infrastructure is written and tested; the mobile clients require a Mac for the iOS toolchain and are paused until that is available.

## Accessibility

TeamTalk Streamer is designed with blind users as the primary audience.

- Every interactive control carries `AutomationProperties.Name` and `HelpText` so NVDA, JAWS, and other screen readers read meaningful labels rather than generic control types.
- Major state transitions (connecting, logged in, joined, streaming, disconnected, errors) are spoken through Tolk.
- OpenAL feedback tones play alongside speech for non-verbal confirmation — ascending chime for successful connections, descending tone for disconnects, low buzz for errors.
- Every flow is reachable by keyboard; no mouse-only actions.

## Requirements

- Windows 10 or 11, 64-bit.
- .NET 10 is embedded in the published executable — no separate runtime install required for end users.
- A TeamTalk 5 server to connect to (self-hosted or a server you have credentials for).
- **A TeamTalk SDK License Key from [BearWare](https://bearware.dk/)** if you intend to use this beyond the 30-day trial of the SDK. The SDK is proprietary and its license is the user's responsibility; TeamTalk Streamer only integrates with it.

## Quick start (end user)

1. Download the latest `TeamTalkStreamer.zip` from the Releases page (or build from source — see below).
2. Extract the zip anywhere you like. Keep `TeamTalkStreamer.exe`, `TeamTalk5.dll`, and `guide.html` together.
3. Run `TeamTalkStreamer.exe`.
4. Click **Server settings…** → fill in the host, nickname, credentials → click **Probe server for channel list** → pick your channel → **Save**.
5. *(Optional)* Click **Excluded apps…** → pick your screen reader from the list of currently-playing apps → **Save**. Its audio will be skipped from the capture so listeners don't hear your screen reader.
6. Click **Start streaming**. The app connects, joins the channel, and begins streaming your default playback device in one flow.
7. Click **Stop streaming** when finished.

Press **F1** at any time to open the full user guide.

## Controls

| Key / button | What it does |
|---|---|
| Start / Stop streaming | One-click toggle — runs the full connect → join → stream flow, or the reverse. |
| Server settings… | Opens the credentials dialog (host, user, channel). |
| Excluded apps… | Opens the per-app exclusion picker (hide one running app's audio from the capture). |
| Master volume slider | Live 0–100 % volume applied to the captured audio before it reaches the channel. |
| F1 | Opens `guide.html` in your default browser. |
| `=` or numpad `+` | Increases the feedback-tone volume by 10 %. |
| `-` or numpad `-` | Decreases the feedback-tone volume by 10 %. |
| Tab / Shift+Tab | Moves focus between controls. |
| Enter | Triggers the default action (Start / Stop on the main window, Save on dialogs). |
| Escape | Closes dialogs (Cancel). |

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned to 10.0.202 via `global.json`).

Before the project can build you must vendor the TeamTalk SDK:

1. Download `tt5sdk_v5.22a_win64.7z` from <https://www.bearware.dk/teamtalksdk/v5.22a/tt5sdk_v5.22a_win64.7z>.
2. Extract and copy the following into `libs/teamtalk/` (create the folder if needed):
   - `Library/TeamTalk.NET/TeamTalk.cs`
   - `Library/TeamTalk.NET/TeamTalkInterop.cs`
   - `Library/TeamTalk_DLL/TeamTalk5.dll`

Optionally drop Tolk's native DLLs into `libs/tolk/` for screen-reader output:

- `Tolk.dll`, `TolkDotNet.dll`, and the per-screen-reader client DLLs (`nvdaControllerClient64.dll`, `SAAPI64.dll`, etc.) from <https://github.com/dkager/tolk/releases>.

Then:

```bash
dotnet build TeamTalkStreamer.slnx
```

Or to run directly:

```bash
dotnet run --project src/TeamTalkStreamer.App/TeamTalkStreamer.App.csproj
```

## Publish a standalone executable

```bash
publish.bat
```

This produces a single self-contained executable plus `TeamTalk5.dll` and `guide.html` in
`src\TeamTalkStreamer.App\bin\Release\net10.0-windows\win-x64\publish\`.

Equivalent manual command:

```bash
dotnet publish src\TeamTalkStreamer.App\TeamTalkStreamer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Package a release zip

After `publish.bat` has succeeded:

```bash
package.bat
```

Produces `TeamTalkStreamer.zip` in the project root containing the entire published folder plus a freshly-copied `guide.html` — ready to upload as a GitHub release asset or share directly.

## Architecture at a glance

The solution is split into seven projects along clean boundaries:

| Project | Responsibility |
|---|---|
| `TeamTalkStreamer.Core` | Abstractions — `IAudioSource`, `IAudioSink`, `AudioRouter`, state enums. No third-party deps. |
| `TeamTalkStreamer.Audio.Windows` | WASAPI loopback capture via NAudio. |
| `TeamTalkStreamer.TeamTalk` | Thin wrapper over the BearWare SDK — partial `TeamTalkClient` + `TeamTalkSink`. |
| `TeamTalkStreamer.MobileBridge` | LAN server for v2 companion apps (currently unreferenced by the App until the iOS client ships). |
| `TeamTalkStreamer.Accessibility` | Tolk speech + OpenAL feedback tones. |
| `TeamTalkStreamer.Persistence` | JSON settings store at `%APPDATA%\TeamTalkStreamer\`. |
| `TeamTalkStreamer.App` | WPF UI, MVVM view models, DI host. |

Everything follows a consistent coding style: `#region`/`#endregion` grouped with nested sub-regions, thorough comments, inheritance + polymorphism through interfaces, partial classes split by concern, enum-driven state tracking, and accessibility properties on every control.

## License

The TeamTalk Streamer source code is available under the terms of the LICENSE file in this repository.

The TeamTalk 5 SDK (`TeamTalk.cs`, `TeamTalkInterop.cs`, `TeamTalk5.dll`) is **proprietary** and copyright BearWare.dk. Distribution of an end-user build requires a License Key purchased from <https://bearware.dk/>. The vendored files in `libs/teamtalk/` are excluded from version control (`.gitignore`) for this reason — each developer must download and license the SDK independently.
