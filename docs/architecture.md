# Architecture

## High-level diagram

```
┌────────────────────────────────────────────────────────────────────────┐
│ Program.Main()  (Program.cs)                                            │
│   └─> Forms.AutoPatcher            self-update from GitHub releases     │
│         └─> Forms.ClientUpdaterForm   load supported client list        │
│               └─> Forms.Container      MDI shell with one tab per tool  │
│                     │                                                   │
│                     ├─ ToggleApplicationStateForm  (ON/OFF + tray)      │
│                     ├─ AutopotForm / AutopotForm(Ygg)                   │
│                     ├─ SkillTimerForm      (3× AutoRefreshSpammer)      │
│                     ├─ ProfileForm                                          │
│                     ├─ AHKForm             (Skill Spammer)             │
│                     ├─ SkillAutoBuffForm / StuffAutoBuffForm            │
│                     ├─ DebuffRecoveryForm                               │
│                     ├─ MacroSongForm / MacroSwitchForm                  │
│                     ├─ ATKDEFForm                                        │
│                     └─ ServersForm                                      │
└────────────────────────────────────────────────────────────────────────┘
        │ observer messages (Subject)                    │ per-profile Actions
        ▼                                                ▼
┌──────────────────────────┐   reads   ┌───────────────────────────────┐
│ Model/* (Actions)        │ ◄──────── │ Client  (Model/Client.cs)     │
│ Autopot, AHK, AutoBuff,  │  memory   │  via Utils/ProcessMemoryReader │
│ StatusRecovery, Macro,   │           │  (ReadProcessMemory)          │
│ ATKDEFMode, …            │ ─────────►│ Game client process (Ragnarök)│
└──────────────────────────┘  sends    └───────────────────────────────┘
        one _4RThread each      input      (PostMessage keydown/click)
```

## Startup sequence

| Step | Code | What happens |
| --- | --- | --- |
| 1 | `Program.cs` | Enables visual styles and runs `Forms.AutoPatcher`. |
| 2 | `Forms/AutoPatcher.cs` | Fetches `https://api.github.com/repos/4RTools/4RTools/releases/latest`, compares the tag with `AppConfig.Version`. If outdated: download the `.rar` asset, rename `4RTools.exe` → `4RTools_old.exe`, extract with Aspose.Zip over the current folder, delete the archive, `Environment.Exit(0)`. In all cases (`finally`) it opens `ClientUpdaterForm`. |
| 3 | `Forms/ClientUpdaterForm.cs` | Builds the in-memory supported-client list: local `supported_servers.json` (`LocalServerManager`) + remote JSON (`AppConfig._4RClientsURL`), falling back to the embedded resource file if the request fails. Each `ClientDTO` becomes a `Client` in `ClientListSingleton`. Then opens `Container`. |
| 4 | `Forms/Container.cs` | Configures itself as an MDI container, attaches itself to the `Subject`, and hosts one child form per feature tab (see the *Frames* region). On load it creates the `Default` profile and lists running game processes and profiles. |
| 5 | User selects a process | `processCB_SelectedIndexChanged` builds a `Client` from the `"Name.exe - PID"` string, stores it in `ClientSingleton`, and broadcasts `MessageCode.PROCESS_CHANGED`. |

## The observer pattern (glue between UI and engines)

`Utils/RObserver.cs` defines a small synchronous observer set:

- `Subject` — holds a list of `IObserver` and a current `Message`.
  `Notify(message)` stores the message and calls `Update(subject)` on every observer.
- `Message` — a `MessageCode` plus an optional `data` payload.
- `MessageCode` values:

  | Code | Meaning |
  | --- | --- |
  | `PROCESS_CHANGED` | A game client was attached (Container reads the character name). |
  | `PROFILE_CHANGED` | A profile was loaded (every feature form re-renders from the profile). |
  | `PROFILE_INPUT_CHANGE` | Defined for future/value-change use. |
  | `TURN_ON` / `TURN_OFF` | The global toggle; each feature starts/stops its thread. |
  | `SHUTDOWN_APPLICATION` | Tray → Close; disables the hook and exits. |
  | `CLICK_ICON_TRAY` | Tray icon double-clicked; restore the main window. |
  | `SERVER_LIST_CHANGED` | Local server list changed; re-render *Servers* tab and process list. |

Who does what:

- **`Container`** owns the single `Subject` instance and passes it to every child form
  constructor. It is itself an observer (updates character name, enables/disables the
  combo boxes on ON/OFF, handles tray/shutdown).
- **`ToggleApplicationStateForm`** installs the global keyboard hook and is the only
  place that broadcasts `TURN_ON` / `TURN_OFF` (and tray/shutdown messages).
- **Feature forms** (`AutopotForm`, `AHKForm`, …) are observers: on `PROFILE_CHANGED`
  they reset their controls and re-render from `ProfileSingleton.GetCurrent()`, on
  `TURN_ON`/`TURN_OFF` they start/stop their `Action`.

> Note: `Notify` is synchronous and loop-based. The keyboard hook callback runs on the
> UI thread (the thread that installed the hook), so toggling ON/OFF happens on the UI
> thread and the observers may safely touch WinForms controls.

## Singletons (`Model/Client.cs`, `Model/Profile.cs`, `Model/Tracker.cs`)

| Singleton | Responsibility |
| --- | --- |
| `ClientListSingleton` | All `Client` objects built from the supported-server list. Used to enumerate clients and resolve a running process to a definition. |
| `ClientSingleton` | The **currently attached** game client. `Instance(client)` stores it; `GetClient()` returns it. |
| `ProfileSingleton` | The active `Profile` plus static CRUD over profile files (`Create`, `Load`, `Delete`, `Rename`, `Copy`, `SetConfiguration`). |
| `TrackerSingleton` | Lazily created `Tracker` that posts analytics events to the 4RTools API. |

## Threading model (`Utils/_4RThread.cs`)

Every automation engine is an `Action` (see [Features](features.md)) that owns one
`_4RThread`:

```csharp
public _4RThread(Func<int, int> toRun)   // wraps a delegate in:
    new Thread(() => {
        while (true) {
            try { toRun(0); }
            catch (Exception ex) { Console.WriteLine(...); }
            finally { Thread.Sleep(5); }   // yields the CPU even on exceptions
        }
    }) { /* STA thread */ }
```

Consequences worth knowing:

- The delegate runs **forever** in a tight loop with a ~5 ms sleep between iterations;
  each engine also sleeps inside its body (e.g. `Autopot.delay`, `AHK.AhkDelay`,
  `AutoRefreshSpammer.RefreshDelay * 1000 ms`).
- Exceptions inside an engine never kill the thread — they are logged to the console and
  the loop continues.
- `_4RThread.Stop(thread)` does not stop the loop; it calls `Thread.Suspend()`.
  Suspend is frozen/deprecated on modern .NET, so this is deliberate tech debt — see
  [Contributing: Known issues](contributing.md#known-issues--tech-debt).
- Because threads are only suspended, all per-action state (eg. the Autopot counter) can
  resume mid-flight, and re-`Start()`ing an action stops the old thread first.

### ON/OFF lifecycle

1. User presses the toggle key (or the ON/OFF button).
2. `ToggleApplicationStateForm.toggleStatus()` flips the button/tray icon and broadcasts
   `TURN_ON` or `TURN_OFF`.
3. Each feature observer reacts, e.g. `AutopotForm` → `ProfileSingleton.GetCurrent().Autopot.Start()/.Stop()`.
4. On `TURN_ON` the Container locks the profile and client combo boxes; on `TURN_OFF` they are unlocked.

## Data flow of a single feature (Autopot)

```
Profile\Default.json ──Load──> ProfileSingleton.profile.Autopot
      ▲                                  │
      │ SetConfiguration(action)         │ Start() on TURN_ON
      │ (saves on every UI change)       ▼
AutopotForm ◄──observer (PROFILE_CHANGED)── _4RThread ──loop──> Client.ReadCurrentHp()
                                                   below %? ──> Interop.PostMessage(client window, WM_KEYDOWN/UP, key)
```

- The form only marshals values between controls and the profile object (`ProfileSingleton.SetConfiguration(...)` persists immediately).
- The engine (`Model/Autopot.cs`) is UI-free: it reads the client and posts input.
- Toggle semantics live in the observer graph, not in the engine.

## External integrations

| Integration | Code | Endpoint |
| --- | --- | --- |
| Auto-update | `Forms/AutoPatcher.cs` | GitHub releases API (`AppConfig._4RLatestVersionURL`) |
| Supported servers | `Forms/ClientUpdaterForm.cs` | `https://storage.googleapis.com/4rtools/supported_servers.json` |
| Advertisements | `Model/Advertiser.cs`, `Forms/AdvertisementForm.cs` | `https://storage.googleapis.com/4rtools/advertisers.json` |
| Analytics | `Model/Tracker.cs` | `POST https://api.4rtools.com.br/api/analytics/public/sendMetrics` |

All network calls are best-effort with try/catch and short timeouts (5 s); failures never
block startup.

## Build-time weaving

`FodyWeavers.xml` enables **Costura.Fody**, which embeds referenced assemblies
(Newtonsoft.Json, Aspose.Zip, …) into `4RTools.exe` so the published tool is a single
self-contained file.
