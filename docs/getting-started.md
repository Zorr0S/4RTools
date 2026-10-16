# Getting Started

## Requirements

| Requirement | Notes |
| --- | --- |
| Windows | The tool is Windows-only (Win32 API access, `System.Windows.Forms`). |
| .NET Framework 4.7.2 | Targeting pack / runtime. Usually already present on Windows 10/11. |
| Visual Studio 2022 | With the **.NET desktop development** workload. This is the IDE the project was created with. |
| Administrator rights | 4RTools must run as admin to open the game process for reading/writing memory. The manifest (`app.manifest`) already requests `requireAdministrator`, so Windows will prompt (or auto-elevate) on launch. |
| A supported Ragnarök client | Memory-based features (Autopot, buffs, status recovery) only work on clients listed in `supported_servers.json`. Spammers/macros work on any client. See [Client Memory & Input](client-memory.md). |

## Building and running

1. Clone the repository:

   ```powershell
   git clone https://github.com/4RTools/4RTools.git
   cd 4RTools
   ```

2. Open `4RTools.sln` in Visual Studio 2022.
3. Let NuGet restore the packages (`packages.config` style — right-click the solution →
   **Restore NuGet Packages** if it does not happen automatically).
4. Select `Debug` or `Release` (platform `AnyCPU`) and press **F5** / **Ctrl+F5**.
5. Accept the administrator elevation prompt.

There is no `dotnet build`/`dotnet run` support — this is a legacy (non-SDK) `.csproj`
that must be built with MSBuild/Visual Studio.

## First-run walkthrough

When the app starts, three forms appear in sequence:

1. **AutoPatcher** (`Forms/AutoPatcher.cs`)
   Compares the version tag of the latest GitHub release with `AppConfig.Version`
   (`v2.10.0`). If they differ, it downloads the release `.rar` asset, swaps the current
   `4RTools.exe` to `4RTools_old.exe`, extracts the new files over the folder, deletes
   the archive and exits so you relaunch with the new build. If nothing goes wrong you
   will barely see this window.

2. **ClientUpdaterForm** (`Forms/ClientUpdaterForm.cs`)
   Loads the list of *supported clients*: first the local `supported_servers.json` next
   to the executable, then the remote file from
   `https://storage.googleapis.com/4rtools/supported_servers.json`. If the remote fetch
   fails, it falls back to the copy embedded in the resources. Each entry becomes a
   `Client` stored in the in-memory client list.

3. **Container** (`Forms/Container.cs`) — the main window
   - On load it creates the **Default** profile (`Profile\Default.json`) if it does not
     exist and populates the *Profile* and *Ragnarok Client* combo boxes.
   - **Select your game client** in the *Ragnarok Client* combo (only running processes
     whose name matches a supported client are listed, shown as `Name.exe - <PID>`).
     Selecting one attaches to the process and reads the character name.
   - **Pick a profile** (or create your own in the *Profiles* tab).
   - Configure each feature tab, then press the **ON/OFF toggle key** (default `End`,
     configurable in the ON/OFF panel) to start/stop all automations — you do not need
     to alt-tab out of the game.

Where things get written:

| File / folder | Created when | Purpose |
| --- | --- | --- |
| `Profile\<name>.json` | First run / profile created | All settings for a profile. |
| `supported_servers.json` | You add a server in the *Servers* tab | Your locally added client definitions (merged with the remote list). |

## Troubleshooting

| Symptom | Likely cause / fix |
| --- | --- |
| "This client is not supported. Only Spammers and macro will works." | Your client's executable is not in `supported_servers.json`. Add it in the **Servers** tab (requires the HP and name memory addresses), or keep using spammer/macro features only. |
| Character name stays `- -` / HP reads 0 | The game window was closed and relaunched after attaching. Re-select the client in the combo and press refresh. |
| "Error while loading the new profile…" | The profile JSON is corrupt. Delete (or fix) `Profile\<name>.json`; the Default profile is recreated on next run. |
| Toggle key does nothing | Focus can be anywhere, but the keyboard hook only works while 4RTools is running. Check the key is bound in the ON/OFF panel and that the app is ON (tray icon = green logo). |
| Nothing sends keys in-game | The game client may be running with higher privileges. Run both the game and 4RTools as administrator. |
| App exits right after launch after an update | Expected: the auto-patcher extracts the new version and calls `Environment.Exit(0)` — simply start 4RTools again. |
