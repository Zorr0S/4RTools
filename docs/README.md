# 4RTools Documentation

This folder contains the technical documentation for **4RTools**, an all-in-one desktop
assistant for Ragnarök Online private servers (Autopot, AHK Spammer, Auto Buffs, Song
Macros, ATK x DEF equipment switching and more).

- **Repository:** <https://github.com/4RTools/4RTools>
- **Website:** <https://www.4rtools.com.br>
- **Discord:** <https://discord.gg/HRWvG5ut>
- **License:** MIT — free to use, modify and redistribute.

## Contents

| Document | What you will find |
| --- | --- |
| [Getting Started](getting-started.md) | Requirements, how to build and run the project, first-run walkthrough and troubleshooting. |
| [Architecture](architecture.md) | Startup flow, MDI shell, observer pattern, threading model, singletons and data flow. |
| [Features](features.md) | Reference for every feature (Autopot, Spammer, Autobuff, Macros, ATK x DEF, …) and the code behind it. |
| [Specs](specs/README.md) | Deeper per-tab specifications (UI layout, data model, behavior, edge cases) for Skill Capture, Skill Bible and Mouse Remap. |
| [Profiles](profiles.md) | How profiles are stored, the JSON schema of a profile file and the profile API. |
| [Client Memory & Input](client-memory.md) | How the tool reads the game client's memory, how supported servers are managed and how keystrokes/clicks are injected. |
| [Codebase Map](codebase-map.md) | A file-by-file tour of the repository. |
| [Contributing](contributing.md) | Conventions, how to add a new feature, known issues and tech debt. |

## Quick facts

| | |
| --- | --- |
| Type | Windows Forms desktop application (WinForms) |
| Framework | .NET Framework 4.7.2 (`WinExe`) |
| Language | C# (VS 2022 project format, non-SDK `4RTools.csproj`) |
| Platform | Windows only, requires **administrator** privileges (see `app.manifest`) |
| Entry point | `Program.cs` → `Forms.AutoPatcher` → `Forms.ClientUpdaterForm` → `Forms.Container` |
| Current version | `v2.11.0` (`Utils/AppConfig.cs`) |
| Key libraries | Newtonsoft.Json 13.0.1, Aspose.Zip 22.10.0 (RAR extraction), Costura.Fody (assembly embedding) |
| Persistence | JSON profile files under `Profile\` and `supported_servers.json` next to the executable |

## The 10-second version

4RTools attaches to a running Ragnarök Online client process, **reads its memory**
(current/max HP and SP, character name and the 100-slot status-effect buffer) and
**sends synthetic keyboard/mouse input** to the game window (`PostMessage`) in response
to what it reads. Everything the user configures — keys, delays, percentages — is saved
per *profile* as JSON. A global low-level keyboard hook lets the user turn all
automations ON/OFF with a single key (default `End`) without leaving the game.
