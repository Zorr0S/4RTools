# Spec: Skill Capture (tab *Skill Capture*)

| | |
| --- | --- |
| Status | Implemented |
| Form | `Forms/StatusCaptureForm.cs` |
| Model | `Model/StatusCapture.cs`, `Model/SkillMap.cs` |
| Tab | `tabStatusCapture` (`Forms/Container.cs#SetStatusCaptureWindow`) |
| Profile key | `StatusCapture` (see [Profiles](../profiles.md)) |
| Persistence | `SkillMap.json`, `CapturedStatusLog.json` — both next to the executable, independent of any single profile |

## 1. Purpose

4RTools' Autobuff engine (`Model/Autobuff.cs`) detects a buff by polling the client's
100-slot status-effect buffer and comparing each slot's numeric id against
`Utils/EffectStatusIDs.cs`. That enum only has the public, stock rAthena/Hercules ids —
it has no entries for a private server's custom skills, and this server's own source was
never confirmed available (see `AGENTS.md`), so there was no authoritative list to add.

Skill Capture is the fallback: instead of requiring server source or a working client
gamefile pipeline (both dead ends — see `AGENTS.md`), it lets a user build the
**status id → skill** association empirically, from a running client, while playing
normally. The output of this tab (`SkillMap.json`) is the input the rest of the app reads
to support a custom skill anywhere a stock one is supported (notably Autobuff — see
[Skill Bible](skill-bible.md), which is the curated front-end built on top of the same
map).

## 2. Scope

In scope:
- Passive logging of every distinct status id ever seen on the attached character.
- A user-editable `StatusId -> {SkillId, SkillName, Note, CooldownSeconds}` map.
- Semi-automatic mapping: bind a hotkey to a skill, press it (or use the in-app Test
  button), let the app diff the status buffer before/after and auto-fill the map.
- Import/export of the map as JSON, for sharing between players of the same server.

Out of scope (see `AGENTS.md` for why): reading skill names/ids/descriptions directly
from the client's GRF files. This tab exists *because* that route is blocked.

## 3. UI layout

Single form, docked top-to-bottom (added in reverse order per WinForms `Dock=Top`
stacking — see code comment in `BuildUi()`):

1. **Skill Triggers** (`Panel`, fixed height 140px)
   - Entry row: Skill ID (`TextBox`, numeric), Skill Name (`TextBox`), **Browse...**
     (opens `SkillPickerForm` to search the RTM skill reference instead of typing),
     Key (read-only `TextBox`, captured via `FormUtils.OnKeyDown`/`OnKeyPress`), **Add
     Trigger**, **Remove Selected**, **Test Selected**.
   - `triggersGrid` (`DataGridView`, read-only, single-row-select): Skill ID, Skill
     Name, Key, Status ID (auto-mapped) [reverse-lookup only, not stored on the
     trigger itself], Last Fired, Last Result (debug). Each row's `Tag` is the backing
     `SkillTrigger`; clicking a row loads it back into the entry fields.
2. **Skill Map toolbar**: **Import Skill Map...**, **Export Skill Map...**.
3. **Skill Map entry row**: Status ID, Skill ID, Skill Name, **Browse...** (same
   picker), Note, **Save Mapping**, **Remove Selected**.
4. **Skill Map grid** (`mapGrid`, fixed height 150px, read-only, single-row-select):
   Status ID, Skill ID, Skill Name, Note, sorted by Status ID. Clicking a row loads it
   into the entry fields above; this is the *only* place a Status ID → Skill link is
   created or edited directly (Skill Bible writes to the same store but through its own
   UI).
5. **Section divider label** — "Captured / Active Statuses - reference only, read-only".
6. **Captured toolbar**: checkbox "Show only currently active", **Clear Captured Log**.
7. **Captured grid** (`capturedGrid`, fills remaining space, read-only): Active Now
   (checkbox column), Status ID, First Seen, Last Seen, Times Seen. Rows where the
   status is currently active are highlighted light green and sorted first.
   Double-clicking a row copies its Status ID (and, if already mapped, Skill ID/Name/
   Note) up into the Skill Map entry row and focuses the Skill Name field.

## 4. Data model

### `CapturedStatus` (`CapturedStatusLog.json`)
```
{ StatusId: uint, FirstSeenUtc: DateTime, LastSeenUtc: DateTime, TimesSeen: int }
```
One row per distinct status id ever observed, regardless of whether it is mapped.

### `SkillMapEntry` (`SkillMap.json`, via `SkillMapManager`)
```
{ StatusId: uint, SkillId: int, SkillName: string, Note: string, CooldownSeconds: int }
```
`StatusId` is the dictionary key (one entry per status id). `CooldownSeconds` (0 =
unset) is read by `Autobuff.cs` to suppress re-pressing a key before the skill's real
cooldown has elapsed, even if the buff's duration is shorter than its cooldown.

### `SkillTrigger` (persisted on the profile, `StatusCapture.triggers`)
```
{ SkillId: int, SkillName: string, Key: string }
```
Does **not** store a Status ID — the association only exists once (if) the watcher below
successfully writes a `SkillMapEntry`. The grid's "Status ID (auto-mapped)" column is
computed by reverse lookup into `SkillMapManager.Entries` on every render, not stored.

### `TriggerDiagnostic` (runtime-only, never persisted)
```
{ LastFiredUtc: DateTime, LastResult: string }
```
Keyed by trigger key string, held in a static dictionary on `StatusCapture`. Exists purely
so the grid can show *why* nothing happened (e.g. "no new status appeared") instead of
silently doing nothing.

## 5. Behavior

### 5.1 Passive capture
While the global toggle is ON (`StatusCapture.Start()` on `TURN_ON`), a `_4RThread`
(`CaptureThread`) loops every 500 ms:
1. Reads all `Constants.MAX_BUFF_LIST_INDEX_SIZE` (100) status slots via
   `Client.CurrentBuffStatusCode(i)`, skipping 0 and the `IGNORE`/`IGNORE_2` sentinel
   values.
2. For each non-empty id: if already in `captured`, bumps `LastSeenUtc`/`TimesSeen`;
   otherwise adds a new `CapturedStatus` entry and marks the tick as having discovered
   something new (triggers a `SaveLog()`).
3. Updates the `currentlyActive` snapshot used by the Captured grid's "Active Now"
   column/filter.

### 5.2 Trigger auto-mapping (passive — on physical keypress)
Still inside the same 500 ms tick, for every registered `SkillTrigger`:
1. Checks the key's physical state via `Interop.IsKeyPhysicallyDown` (⚠️ not
   `System.Windows.Input.Keyboard.IsKeyDown` — that API only reflects what a
   message-pumping thread has seen, and this is a plain background thread; see
   `AGENTS.md`/code comments for the investigation that found this).
2. On a rising edge (down now, was up last tick): records a `TriggerDiagnostic`
   ("key seen, waiting on cast window...") and spawns `WatchForNewStatusAfterTrigger`
   with the current status snapshot as the "before" set.

### 5.3 Trigger auto-mapping (explicit — Test button)
`BtnTestTrigger_Click` → `StatusCapture.TestTrigger(trigger)`:
1. Requires a selected trigger row and an attached client (both validated with a
   `MessageBox` otherwise).
2. Snapshots "before" status ids.
3. Sends the trigger's key to the game itself via `Interop.PostMessage` (the same
   mechanism Autopot/AHK/Autobuff already use) — this exists specifically because
   physical-keypress detection (5.2) proved unreliable in practice; sending the key
   itself means 4RTools knows exactly when the press happened with zero detection
   involved.
4. Calls the same `WatchForNewStatusAfterTrigger` diff as 5.2.

### 5.4 The before/after diff (shared by 5.2 and 5.3)
On a separate short-lived background `Thread`:
1. Sleeps `TriggerCaptureWindow` (3 seconds, fixed) to cover cast/animation time.
2. Snapshots "after" status ids, computes `newlyAppeared = after - before`.
3. Exactly 1 new id → `SkillMapManager.SetSkill(id, trigger.SkillId, trigger.SkillName)`,
   result = `"mapped status {id}"`.
4. 0 new ids → left unmapped, result explains it may not be a self-buff, or the window
   was too short.
5. 2+ new ids → left unmapped as ambiguous (something else also procced a status in the
   window) rather than guessing.
6. Either way, writes the `TriggerDiagnostic` and fires `StatusCapture.TriggersChanged`.

### 5.5 Manual mapping
`BtnSaveMapping_Click`: validates Status ID (required, numeric) and at least one of
Skill ID/Skill Name, then `SkillMapManager.SetSkill(...)`. Existing `CooldownSeconds` for
that status id is preserved across re-saves (see `SkillMapManager.SetSkill`).

### 5.6 Import/export
- **Export**: writes the full current map as indented JSON via a `SaveFileDialog`
  (default name `SkillMap.json`).
- **Import**: via `OpenFileDialog`, then a Yes/No/Cancel prompt — Yes overwrites a
  locally-assigned entry on a conflicting Status ID, No keeps the local one, Cancel
  aborts entirely. Reports how many entries were actually applied.

## 6. Cross-cutting / integration

- **Observer wiring**: `SkillMapManager.Changed` and `StatusCapture.TriggersChanged` are
  static `System.Action` events fired from (possibly) background threads. `Container.cs`
  is the single subscriber for both; it marshals onto the UI thread via `BeginInvoke` and
  re-broadcasts as `MessageCode.SKILL_MAP_CHANGED` / `SKILL_TRIGGERS_CHANGED` through the
  app's existing `Subject`/`IObserver` pub-sub, so every form (this one, Skill Bible,
  Autobuff - Skills) reacts the same way as any other cross-form update — no per-form
  event subscription or polling timer. The Captured grid is the one exception: it reflects
  genuinely continuous state (the live status buffer), so it keeps a 1-second `Timer`.
- **Feeds Autobuff**: every `SkillMapManager` entry appears in the "Custom / Mapped
  Skills" section of *Autobuff - Skills* with its own key-bind box, writing into the same
  `AutoBuff.buffMapping` dictionary the hardcoded job-class skills use. No changes to
  `Autobuff.cs`/`EffectStatusIDs.cs` were needed: the enum is a plain `uint`-backed enum,
  so casting an arbitrary captured status id to it is legal C# even with no matching named
  member.
- **Feeds cooldown enforcement**: `Autobuff.cs`'s press loop looks up
  `SkillMapManager.Get((uint)status)` and skips re-pressing the key if `CooldownSeconds`
  hasn't elapsed since the last press.
- **Shared with Skill Bible**: both tabs write to the same `SkillMapManager`/trigger list;
  Skill Bible is effectively a curated, RTM-database-backed alternate front-end for the
  same underlying data (see [Skill Bible spec](skill-bible.md)).

## 7. Edge cases / validation

- Status buffer value `0` or the `IGNORE`/`IGNORE_2` sentinels are never logged — they are
  the buffer's empty-slot representation, not a real status.
- A trigger key that fails to parse as a `Keys` enum value is skipped in the capture loop
  (malformed/unsupported key name) rather than throwing.
- Grid rebuilds (`RenderMapGrid`, `RenderTriggers`, `RenderCapturedGrid`) use
  `PreserveGridState` to restore the selected row (by `Tag` equality) and scroll position
  after a full `Rows.Clear()` + rebuild, since there is no cheap way to diff rows against
  the underlying dictionaries.
- "Clear Captured Log" only clears `CapturedStatusLog.json` (the discovery log) — it never
  touches `SkillMap.json` (the confirmed mappings); a confirmation `MessageBox` makes this
  explicit.
- A corrupt `SkillMap.json`/`CapturedStatusLog.json` on load is treated as empty rather
  than crashing app startup.

## 8. Known limitations

- The 3-second capture window (`TriggerCaptureWindow`) is fixed, not configurable; a skill
  with a longer cast/animation time before its status lands will show "no new status
  appeared" even though it is a real buff.
- No icon support for mapped skills from this tab alone — `Model/Buff.cs`'s hardcoded
  catalog and `RTMSkillReference`/`CustomSkillIcons` (used by Skill Bible) are the only
  icon sources.
