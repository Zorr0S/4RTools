# Features

Every feature follows the same contract (`Model/Action.cs`):

```csharp
public interface Action
{
    void Start();               // called on global toggle ON
    void Stop();                // called on global toggle OFF
    string GetConfiguration();  // JSON persisted into the profile file
    string GetActionName();     // the key this action uses inside the profile JSON
}
```

The UI form for a feature is an `IObserver`: it re-renders on `PROFILE_CHANGED` and
starts/stops the action on `TURN_ON`/`TURN_OFF` (see
[Architecture](architecture.md#the-observer-pattern-glue-between-ui-and-engines)).

> Note: the interface is confusingly named `Action` — it is *not* `System.Action`.

## Global ON/OFF toggle

- **Code:** `Forms/ToggleApplicationStateForm.cs`, `Utils/KeyboardHook.cs`
- **Profile key:** `UserPreferences.toggleStateKey` (default `End`)
- The form installs a global low-level keyboard hook (`WH_KEYBOARD_LL`) so the key works
  even while the game window is focused. Pressing it flips the ON/OFF state, updates the
  tray icon (green = on, red = off), plays an optional sound cue and broadcasts
  `TURN_ON`/`TURN_OFF`.
- If no client is attached when turning ON, it shows "Please select the Ragnarök
  Client!!" and stays OFF.
- The tray icon double-click restores the window; its context menu has **Close** which
  broadcasts `SHUTDOWN_APPLICATION`.

## Autopot

- **Code:** `Model/Autopot.cs`, form `Forms/AutopotForm.cs` (tab *Autopot*)
- **Profile keys:** `Autopot` and `AutopotYgg`
- Watches HP and SP read from the client's memory and presses the configured potion key
  when the value drops below the configured percentage.
- Behaviour: while HP % is below the threshold it presses the HP key (every iteration);
  SP is checked independently and the SP key pressed when below its threshold. The
  default cycle delay is 15 ms.
- While the user holds **Alt** the spammer pauses (so chat/typing is never interrupted).
- **Yggdrasil** is a second instance of the same engine on its own tab with its own
  defaults (50 ms) and its own profile key — used on servers with different potion
  macros.

## AHK Spammer (Skill Spammer)

- **Code:** `Model/AHK.cs`, form `Forms/AHKForm.cs` (tab *Skill Spammer*)
- **Profile key:** `AHK20`
- For each mapped key, while the key is **held down** the engine repeatedly posts
  `WM_KEYDOWN` to the game window every `AhkDelay` ms (default 10 ms).
- Each entry is a three-state checkbox: *unchecked* = off, *checked* = spam with left
  click, *indeterminate* = spam without click.
- Options:
  - **Mouse flick** — after each click the cursor is moved 1 pixel diagonally and back;
    some clients only register ground/monster clicks when the cursor moves.
  - **No shift** — taps `Shift` (`keybd_event`) around the spam; helps on servers that
    require a modifier.
  - **Mode** (`ahkMode`):
    - `ahkCompatibility` (default) — clicks are posted to the game window via
      `PostMessage` (works on background windows).
    - `ahkSpeedBoost` — clicks use the real `mouse_event` at the current cursor position
      (higher rate, requires the game window focused). Mouse flick / no-shift are
      disabled in this mode.
- CPU note (from the CHANGELOG): v1.1.0 reduced CPU usage from >5% to <1% by tuning the
  click loop.

## Auto Refresh Spammer (Skill timers)

- **Code:** `Model/AutoRefreshSpammer.cs`, form `Forms/SkillTimerForm.cs` (tab *Skill timers*)
- **Profile keys:** `AutoRefreshSpammer01`, `AutoRefreshSpammer02`, `AutoRefreshSpammer03`
- Three independent timers that press a key every *N* seconds while the automation is ON
  (used to refresh buffs/skills that expire). `RefreshDelay` is in seconds; 0 falls back
  to 1 s.

## Autobuff — Skills / Stuffs

- **Code:** `Model/Autobuff.cs`, forms `Forms/SkillAutoBuffForm.cs` (tab *Autobuff - Skills*)
  and `Forms/StuffAutoBuffForm.cs` (tab *Autobuff - Stuffs*)
- **Profile key:** `Autobuff`
- The engine scans the 100 status-effect slots in the client's memory (see
  [Client Memory & Input](client-memory.md#memory-layout)) and compares them with the
  buff-to-key mapping. Buffs that are **missing** get their key pressed.
- Special cases:
  - `OVERTHRUSTMAX` (Maximum Power-Thrust) suppresses a mapping for plain `OVERTHRUST`.
  - While `QUAGMIRE` is active, Concentration / Increase Agility / True Sight /
    Adrenaline / Spear Quicken are skipped.
  - Buffs are only recast while HP ≥ 20 (`Constants.MINIMUM_HP_TO_RECOVER`).
- The UI generates one icon + key box per buff from `Model/Buff.cs` (the catalog pairs a
  display name and icon with each `EffectStatusIDs` value), five icons per row.
- The **Stuffs** tab is the same UI over a different category of the catalog (potions,
  foods, boxes, scrolls, elementals, candies…).

## Status Recovery (auto-cure)

- **Code:** `Model/StatusRecovery.cs`, form `Forms/DebuffRecoveryForm.cs` shares the tab *Debuff*
- **Profile key:** `StatusRecovery`
- Scans the status buffer for bad statuses (Poison, Silence, Blind, Confusion, Curse,
  Hallucination, …) and presses the mapped cure item key (e.g. Green Potion / Panacea)
  when a matching status is found.
- `autoStand` option presses `Insert` to stand up when the *Sit* status is detected.

## Debuff Recovery

- **Code:** `Model/DebuffsRecovery.cs`, form `Forms/DebuffRecoveryForm.cs` (tab *Debuff*)
- **Profile key:** `DebuffsRecovery`
- Same engine idea as Status Recovery but driven by the debuff catalog (Critical Wound,
  Freezing, Bleeding, Stun, Deep Sleep, Decrease Agi…), letting you map a cure key per
  debuff.

## Song Macro

- **Code:** `Model/Macro.cs` (`ACTION_NAME_SONG_MACRO = "SongMacro2.0"`), form `Forms/MacroSongForm.cs` (tab *Macro Songs*)
- A macro is a `ChainConfig`: an optional **trigger key**, an optional **instrument/dagger
  key** (equip swap pressed before the sequence), and an ordered list of macro entries
  (`MacroKey`: key + delay + optional click). Entry keys are stored in a dictionary keyed
  `in<i>mac<id>` so ordering is guaranteed at execution time.
- While the trigger key is held, the engine plays the sequence in order: instrument key →
  (delay) skill key → optional click → dagger key.
- Designed for Bard/Dancer songs (equip instrument → cast → equip dagger) and any
  multi-step cast rotation.

## Macro Switch

- **Code:** `Model/Macro.cs` (`ACTION_NAME_MACRO_SWITCH = "MacroSwitch2.0"`), form `Forms/MacroSwitchForm.cs` (tab *Macro Switch*)
- Same `Macro` engine with a different number of lanes and a different profile key, used
  to **switch between multiple macro chains** (e.g. one per situation) and bind them to
  different triggers.

## ATK x DEF Mode

- **Code:** `Model/ATKDEFMode.cs`, form `Forms/ATKDEFForm.cs` (tab *ATK x DEF Mode*)
- **Profile key:** `ATKDEFMode`
- Hold the spammer key: the engine first presses all **ATK equipment** keys (e.g. swap
  into damage gear), then spams the attack key (with or without click), and after the
  key is released presses all **DEF equipment** keys to swap back. Useful to stay in DEF
  gear while idle but attack in ATK gear during a burst.

## Profile management

- **Code:** `Model/Profile.cs`, `Forms/ProfileForm.cs` (tab *Profiles*)
- Create / rename / copy / delete profiles. `Default` is protected from delete/rename.
- See [Profiles](profiles.md) for the storage format.

## Server manager

- **Code:** `Model/LocalServerManager.cs`, `Forms/ServersForm.cs` (tab *Servers*),
  `Forms/AddServerForm.cs`
- Add or delete local client definitions (process name, HP address, name address) that
  are merged with the remote supported-servers list. Addresses must be exactly 8 hex
  characters. See [Client Memory & Input](client-memory.md#adding-a-supported-server).

## Skill Capture

- **Code:** `Model/StatusCapture.cs`, `Model/SkillMap.cs`, form
  `Forms/StatusCaptureForm.cs` (tab *Skill Capture*)
- **Profile key:** `StatusCapture`
- Diagnostic/research feature, not an automation: while the global toggle is ON, scans all
  100 status slots (`Client.CurrentBuffStatusCode`) every 500 ms and logs every distinct
  status id ever seen (first seen, last seen, times seen) to `CapturedStatusLog.json` next
  to the executable. Lets a user identify a private server's **custom** skills/buffs (ones
  not in the public rAthena/Hercules `EffectStatusIDs` list) just by playing normally and
  triggering them, instead of hand-probing memory.
- A second, independent JSON file (`SkillMap.json`, via `SkillMapManager`) maps
  `StatusId -> { SkillId, SkillName, Note }` - the point of the whole feature is the
  **EffectStatusID -> SkillID** association (`SkillId` is the actual RO numeric skill id,
  e.g. as used in `skill_db`); `SkillName` is just a human-readable label alongside it,
  since most players recognize a skill by name, not number. The grid in *Skill Capture*
  cross-references captured ids against this map; editing the "Skill ID"/"Skill
  Name"/"Note" cells writes straight to it. The map can be exported to share with other
  players of the same server, or imported from a file someone else built (with a prompt on
  import to overwrite or keep locally-assigned values on a conflicting status id).
- **Auto-mapping via Skill Triggers:** instead of only labeling ids after the fact, the
  user can register a `SkillTrigger` (skill id + skill name + hotkey, same key-capture UX
  as Autobuff/Debuff Recovery) for a skill they know is a buff. `StatusCapture`'s loop
  edge-detects that key being pressed, snapshots the currently-active status ids, waits a
  3-second cast/animation window, snapshots again, and diffs. Exactly one new status id =>
  auto-written into `SkillMapManager` under that skill id/name, no manual lookup needed.
  Zero or multiple new ids (something else also procced a status in that window) is left
  unmapped rather than guessed, same as anything the passive scan finds unlabeled.
- **Feeds directly into Autobuff:** the *Autobuff - Skills* tab (`Forms/SkillAutoBuffForm.cs`)
  has a "Custom / Mapped Skills" section below the hardcoded job-class groups, listing
  every `SkillMapManager` entry with a key-bind textbox - same key-capture UX as the
  hardcoded skills, writing into the very same `AutoBuff.buffMapping` dictionary. This
  works because `EffectStatusIDs` is just a `uint`-backed C# enum: casting an arbitrary
  status id to it (even one with no named member) is legal and behaves identically to a
  "real" one everywhere `AutoBuff`'s engine uses it, so **no changes to `AutoBuff.cs` or
  the enum were needed** - only a renderer for this one dynamic section, since
  `SkillMap.json` can grow at any time (new captures, auto-mapped triggers, imports) and
  `BuffRenderer`'s icon grid is a render-once API not safe to call twice. Mapped skills
  have no icon (no client gamefile pipeline feeding this - see `AGENTS.md`), so this
  section is a plain name+key list instead of icons.
- See `AGENTS.md` for the fuller background on why this whole feature exists (reading
  skill data from the client's GRF files turned out to be blocked by an incomplete
  GRF-decryption library, and the server's own source was never confirmed available).
