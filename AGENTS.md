# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, etc.) working in this repository.
Humans: see [docs/README.md](docs/README.md) instead, it's the same information with more
room to breathe.

## What this is

**4RTools** — a Windows Forms desktop assistant for Ragnarök Online private servers
(Autopot, AHK Spammer, Autobuff, Song Macros, ATK x DEF switching, …). It attaches to a
running RO client process, reads its memory, and sends synthetic keyboard/mouse input.

| | |
| --- | --- |
| Type | WinForms desktop app |
| Framework | .NET Framework 4.7.2 (`WinExe`), non-SDK `4RTools.csproj` (VS2022 project format) |
| Package management | **`packages.config` style, not `PackageReference`**. See *Adding a dependency* below — this matters. |
| Entry point | `Program.cs` → `Forms.AutoPatcher` → `Forms.ClientUpdaterForm` → `Forms.Container` |
| Deep docs | [docs/architecture.md](docs/architecture.md), [docs/features.md](docs/features.md), [docs/getting-started.md](docs/getting-started.md) |

## Building

No `dotnet build`/`dotnet run` support — this is a legacy non-SDK csproj, it needs real
MSBuild:

```powershell
# Locate MSBuild (VS2022/2026 install) if it's not on PATH:
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe

# First build on a fresh machine needs packages.config restored (packages/ is gitignored).
# `MSBuild /t:restore` alone does NOT restore packages.config-only projects reliably —
# use nuget.exe explicitly:
nuget.exe restore 4RTools.sln -NonInteractive

& $msbuildPath "4RTools.csproj" /t:Build /p:Configuration=Debug
```

If `nuget.exe` isn't present, fetch the official CLI from
`https://dist.nuget.org/win-x86-commandline/latest/nuget.exe` — that's the canonical
Microsoft distribution point, safe to script.

## Adding a dependency — do NOT use `<PackageReference>`

This project mixes `packages.config` (all existing deps: Newtonsoft.Json, Aspose.Zip,
Costura.Fody/Fody, a long tail of `System.*` polyfills for net472) with classic
`<Reference Include="..."><HintPath>...</HintPath></Reference>` entries in the `.csproj`.

**Adding a `<PackageReference>` item to this csproj flips NuGet's restore-style detection
for the whole project and silently stops restoring `packages.config`** (confirmed by
hitting a missing `Fody.targets` build error after doing exactly this). The fix was to
revert to the existing convention:

1. Add the package to `packages.config` (alphabetical by id, `targetFramework="net472"`).
2. `nuget.exe restore 4RTools.sln -NonInteractive` to pull it into `packages/`.
3. Add a `<Reference Include="PackageName">` with an explicit `<HintPath>packages\Pkg.Version\lib\<tfm>\Pkg.dll</HintPath>` in `4RTools.csproj`, in the big `<ItemGroup>` alongside the other `Reference` entries (~line 60 onward). Pick whichever `lib\<tfm>` folder is closest to net472 (prefer `net4x` > `netstandard2.0` > lower).
4. **Transitive dependencies are not auto-copied** under packages.config — if the package depends on others (e.g. `GRF.dll` needs `Ionic.Zlib.Core` and `System.Memory`, resolved into the global cache at `~/.nuget/packages/<id>/<version>/lib/...` but not into the local `packages/` folder), you must add explicit `<Reference>`/`<HintPath>` entries for those too once code actually calls into them, or it'll throw `FileNotFoundException` at runtime despite compiling fine.
5. Rebuild and confirm a clean `Build succeeded` before moving on — don't assume it from the restore step alone.

## In-progress: reading skill data from client gamefiles

Goal: stop hand-maintaining `Model/Buff.cs`'s ~150 hardcoded skill/buff entries
(name + `Utils/EffectStatusIDs.cs` enum value + icon `Bitmap`) and instead read skill
names/ids/descriptions/icons directly from the RO client's own game files.

Target install used for exploration: `D:\RO\Refuge - Copy` ("Return to Morroc" /
Project RM — `PRM.exe`, `prmbase.grf`). Confirmed facts about it:

- GRF v2.0 format (standard, well-supported). Load order (`DATA.INI`):
  `extraupdates.grf` → `prmbase.grf` → `data.grf` (highest priority first — a custom
  server's overrides, if any, live in `extraupdates.grf`).
- Skill data (`skillinfolist.lub`, `skillid.lua`, `skilldescript.lub`, expected under
  `data\luafiles514\lua files\skillinfoz\`) is **not** loose in `System/` on this
  install — it's packed inside one of the three GRFs above, needs extracting.
- `.lub`/`.lua` client data files are **plain Lua 5.1 bytecode** (header `1B 4C 75 61 51`
  = `ESC L u a` + `Q`, where `Q` = 0x51 is Lua's own 5.1 version byte — not a modified/
  obfuscated RO-specific format). Even files with a `.lua` extension on this client are
  bytecode, not source.

Tooling for this (see *Adding a dependency* above for how `GRF`/`MoonSharp` were wired in):
- **`GRF` 0.3.1** NuGet package — reads GRF archives (`GRF.Grf` class: `.Load(path)`,
  `.Entries` / `.EntryNames` dictionary of `GrfEntry`, `GrfEntry.GetUncompressedData()`).
  Still referenced in the `.csproj`/`packages.config` (low-risk to leave; nothing calls it).
- **`MoonSharp` 2.0.0** NuGet package — pure C# Lua interpreter, would execute the
  *decompiled* Lua source (not the raw bytecode) and read back real tables as C# objects.
  Also still referenced, also unused.
- **unluac** (MIT, <https://unluac.sourceforge.net>) — standalone Lua 5.0–5.4 decompiler,
  would turn the `.lub` bytecode into readable Lua source via a separate Java CLI
  (`java -jar unluac.jar <file>`, needs a JRE). The actual jar
  (`tools/unluac/unluac_2025_12_23.jar`) was deleted in a later repo cleanup pass since
  the whole pipeline below was abandoned (see status note) — re-download from the link
  above if this route is ever revisited.

Pipeline (never actually completed - blocked, see status note): GRF extract
(highest-priority archive wins) → `unluac` decompile → MoonSharp executes the resulting
source → walk the resulting Lua table into a C# model.

**What's achievable from client files alone (no server source needed):** full skill
catalog — id, internal `SKID` name, display name, description, max level, job-tree
placement, icon. Any custom skills this server added must be in here too, or they
couldn't render in the client's skill tree at all.

**What needs server source (rAthena/Hercules fork), not just the client:** mapping a
skill to the `EffectStatusIDs` value it triggers (what `Buff.cs`'s autobuff detection
actually keys off of) — that's a server-side definition (`status.hpp` / `skill_db`), not
present in client data, **for custom skills only** (stock skills' EFST ids are already
public rAthena/Hercules knowledge regardless of this server's source). Also: skill
cooldown/cast time/SP cost/damage formulas are enforced server-side and not reliably in
client data.

**Status as of writing:** still unconfirmed whether the GM ever open-sourced the server
(they lost their Discord account/history, so there's no way to check their old messages;
someone close to the original GM is being asked directly). Given the odds, the decision
was made to stop waiting on it and build the manual-observation fallback as a real feature
now rather than block further progress on an answer that may never come.

**Implemented — "Skill Capture" feature** (`Model/StatusCapture.cs`,
`Model/SkillMap.cs`, `Forms/StatusCaptureForm.cs`, tab `tabStatusCapture` in
`Forms/Container.cs`/`Container.Designer.cs`, wired into `Model/Profile.cs`):

- `StatusCapture` is an `Action` like `AutoBuff`/`StatusRecovery` — on `TURN_ON` it scans
  all `Constants.MAX_BUFF_LIST_INDEX_SIZE` (100) status slots every 500ms via
  `Client.CurrentBuffStatusCode(i)`, and logs every distinct non-empty status id ever seen
  (first seen, last seen, times seen) to `CapturedStatusLog.json` next to the exe. This
  runs passively while the user plays normally — no manual memory-probing needed anymore,
  it's just always collecting.
- `SkillMapManager` (`Model/SkillMap.cs`) is a separate, independent JSON file
  (`SkillMap.json`, same next-to-exe convention as `supported_servers.json`) mapping
  `StatusId -> { SkillId, SkillName, Note }`. **The actual goal of this whole feature is
  the EffectStatusID -> SkillID association** — `SkillId` is the real RO numeric skill id
  (as used in `skill_db`/skill scripts), `SkillName` is just a human-readable label since
  most people know a skill by name rather than number. This is the "premade map" the user
  asked for — it can be hand-built from the UI, or imported wholesale from a JSON file
  someone else put together (e.g. shared by another player of the same server), with a
  choice on import to overwrite locally-assigned values or keep them.
- `Forms/StatusCaptureForm.cs` (tab **"Skill Capture"**) is a plain code-built form (no
  `.Designer.cs`/`.resx` — simpler than hand-authoring WinForms designer XML blind) showing
  a live grid of captured status ids cross-referenced with the current skill map, with
  editable "Skill Name"/"Note" cells (writes straight to `SkillMapManager`) and
  Import/Export/Clear buttons.
- **Auto-mapping (`SkillTrigger`):** the user can also bind a hotkey to a known buff skill
  (skill id + skill name) instead of only labeling captured ids after the fact.
  `StatusCapture.CaptureThread` edge-detects that key via
  `System.Windows.Input.Keyboard.IsKeyDown` on every 500ms tick (matching the
  key-state-check style `AutoBuff.useAutobuff` already uses — no `KeyboardHook.Add`
  involved, deliberately: avoids global-hook lifecycle/thread-affinity complexity for a
  feature that only needs ~500ms resolution anyway). On the rising edge it snapshots
  currently-active status ids, waits a 3s window on a short-lived background thread,
  snapshots again, and diffs — exactly one new id auto-writes `{SkillId, SkillName}` into
  `SkillMapManager`; zero or several are left unmapped rather than guessed.
- **Wired into Autobuff** (`Forms/SkillAutoBuffForm.cs`): a "Custom / Mapped Skills"
  section below the hardcoded job-class `GroupBox`es lists every `SkillMapManager.Entries`
  value with a key-bind textbox, writing into the same `AutoBuff.buffMapping` dictionary
  the hardcoded skills use (`ProfileSingleton.GetCurrent().Autobuff.AddKeyToBuff(...)`).
  No change to `Model/Autobuff.cs` or `Utils/EffectStatusIDs.cs` was needed for this:
  `EffectStatusIDs` is a plain `uint`-backed enum, so casting an arbitrary captured status
  id to it (`(EffectStatusIDs)entry.StatusId`) is legal C# even with no matching named
  member, and `AutoBuffThread`'s loop already compares by that cast value — it was already
  generic over "any status id," the hardcoded `Buff.cs` lists were just the only thing
  feeding it one before. The one thing that did need new code: this section re-renders on
  a 2s timer (and on `PROFILE_CHANGED`) because `SkillMap.json` can grow at any time from
  captures/triggers/imports while the tab is already open, and `BuffRenderer`'s icon-grid
  renderer is write-once — calling it twice would duplicate every control in every section,
  so the custom section manages its own `Controls.Clear()`/rebuild instead of reusing it.
  No icons for mapped skills (nothing feeds those in yet), so it's a name+key list rather
  than `BuffRenderer`'s icon grid.

The client-gamefile pipeline (GRF extract → unluac decompile → MoonSharp parse) described
above is still blocked on the `GRF` 0.3.1 package's decryption bug (see git history around
this file's introduction for the full investigation) and is now deprioritized — it would
only ever have gotten skill *names/descriptions*, not the EFST ids that actually matter for
autobuff, and the capture approach gets those directly with zero dependency on either the
server source or a working GRF decryptor. Revisit it only if hand-labeling via Skill
Capture proves too slow.
