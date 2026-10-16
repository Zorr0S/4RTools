# Spec: Mouse Remap (tab *Mouse Remap*)

| | |
| --- | --- |
| Status | Implemented |
| Form | `Forms/MouseRemapForm.cs` |
| Model | `Model/MouseRemap.cs` |
| Tab | `tabMouseRemap` (`Forms/Container.cs#SetMouseRemapWindow`) |
| Profile key | `MouseRemap` (see [Profiles](../profiles.md)) |

## 1. Purpose

Replaces a standalone AutoHotkey script
(`C:\Users\zorro\Documents\AHK2\M4ToI.ahk`: `XButton1::Send("i")`,
`XButton2::Send("o")`) the user was running alongside 4RTools just to remap the two mouse
side (back/forward) buttons to keyboard keys. Brings that one piece of functionality
natively into the app instead of requiring a second always-running tool.

## 2. UI layout

Single form, plain controls (no `.Designer.cs`/`.resx`, same convention as
`StatusCaptureForm`/`SkillBibleForm`):
- Hint label explaining the feature.
- **Side Button 1 (Back / XButton1):** — read-only `TextBox`, key captured via
  `FormUtils.OnKeyDown`/`OnKeyPress` (same key-bind UX used everywhere else in the app:
  press a key while focused, it fills the box).
- **Side Button 2 (Forward / XButton2):** — same, independent key.

No separate "Save" button — each `TextBox.TextChanged` writes straight through to the
profile (§4).

## 3. Data model

`MouseRemap : Action` (persisted as the profile's `MouseRemap` key):
```
{ XButton1Key: Key, XButton2Key: Key }  // System.Windows.Input.Key, default Key.None
```

## 4. Behavior

### 4.1 Binding a key
On `TextChanged` for either box: parses the text back into a `System.Windows.Input.Key`
(`Enum.Parse`), validates it with `FormUtils.IsValidKey` (falls back to `Key.None` if
invalid/unparseable — mirrors the exact pattern used by every other key-capture field in
the app), writes it onto `ProfileSingleton.GetCurrent().MouseRemap`, and persists via
`ProfileSingleton.SetConfiguration(...)`. Any parse failure is swallowed (`catch { }`) —
the field simply doesn't update, rather than throwing while the user is still mid-keypress
capture.

### 4.2 Runtime polling
`MouseRemap.Start()` (on `TURN_ON`, with a client attached) spins up a `_4RThread`
(`RemapThread`) polling every 15 ms:
1. Reads `Interop.IsKeyPhysicallyDown(Keys.XButton1)` and `...XButton2` — **not**
   `System.Windows.Input.Keyboard.IsKeyDown`, for the same reason documented in
   [Skill Capture §5.2](skill-capture.md#52-trigger-auto-mapping-passive--on-physical-keypress):
   that API only reflects what a message-pumping thread has seen, and this runs on a
   plain background `Thread` with no message pump. `Keys.XButton1`/`XButton2` are
   standard Win32 virtual-key codes, usable directly with `GetAsyncKeyState`.
2. On a rising edge (button down now, was up last tick) for either button, calls
   `SendKey(configuredKey)`.
3. `SendKey`: no-op if the configured key is `Key.None`; otherwise resolves the attached
   client and posts the key via `Interop.PostMessage` with `Constants.WM_KEYDOWN_MSG_ID` —
   the same mechanism `Autopot`/`AHK`/`Autobuff`/`StatusCapture.TestTrigger` all use to
   send synthetic keystrokes to the game window. A malformed/unmappable key string is
   silently skipped (nothing sensible to send).

`MouseRemap.Stop()` (on `TURN_OFF`) stops the thread via `_4RThread.Stop`.

## 5. Cross-cutting / integration

- **Observer wiring**: `Update(ISubject)` handles `PROFILE_CHANGED` (reload both key
  boxes from the newly-loaded profile), `TURN_ON` (`Start()`), `TURN_OFF` (`Stop()`) — the
  standard `Action`/`IObserver` contract every feature in the app follows (see
  [Features](../features.md) and
  [Architecture](../architecture.md#the-observer-pattern-glue-between-ui-and-engines)).
  No bespoke events needed — unlike Skill Capture/Skill Bible, this tab's state only ever
  changes from its own UI, never from a background watcher, so there was nothing to bridge
  through `Container.cs`.
- Independent of Skill Capture/Skill Bible/Autobuff — does not read or write
  `SkillMap.json` or `StatusCapture.triggers`; it is a standalone input-remapping feature.

## 6. Known limitations

- Polling-based (15 ms interval), unlike AHK's hook-based interception: the OS still
  delivers the native XButton1/XButton2 click to whatever window has focus (e.g. the
  browser "back" gesture) in addition to 4RTools sending the remapped key — AHK's
  `::Send` replaces the native action, this only adds to it. Acceptable for this use case
  (remapping for in-game use while the RO client is focused) but not a drop-in replacement
  for every AHK remap use case.
- No support for mouse buttons beyond XButton1/XButton2 (e.g. the scroll wheel click,
  additional side buttons on gaming mice) — scoped to exactly what the AHK script it
  replaces did.
- 15 ms poll interval is hardcoded, not user-configurable.
