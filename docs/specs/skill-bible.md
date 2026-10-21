# Spec: Skill Bible (tab *Skill Bible*)

| | |
| --- | --- |
| Status | Implemented |
| Form | `Forms/SkillBibleForm.cs` |
| Model | `Model/RTMSkillReference.cs`, `Model/RTMClassProgression.cs`, `Model/CustomSkillIcons.cs`, `Model/SkillMap.cs`, `Model/StatusCapture.cs` |
| Tab | `tabSkillBible` (`Forms/Container.cs#SetSkillBibleWindow`) |
| Data source | `Resources/RTMSkills/skills-reference.json` + `Resources/RTMSkills/icons/*.png` (bundled `Content`, scraped from the server's public community database — see `AGENTS.md`) |
| Persistence written by this tab | `SkillMap.json` (via `SkillMapManager`), profile's `StatusCapture.triggers`, `CustomSkillIcons/*.png` |

## 1. Purpose

[Skill Capture](skill-capture.md) requires already knowing a skill exists and manually
typing/discovering its id. Skill Bible is the browsable alternative: the *entire* scraped
skill catalog for this server (513 entries, names/classes/types/icons sourced from the
community database — see `AGENTS.md`) as one filterable table, so a user can go from
"browse what my class has" to "key bound, cooldown set, tested, wired into Autobuff"
without leaving the tab or typing an id by hand.

## 2. UI layout

1. **Filter bar** (`Panel`, 34px): Class `ComboBox` (`"All Classes"` + every distinct
   `RTMSkillRef.Class`, ordered by `RTMClassProgression.RankOf` — i.e. job-tree order,
   Orphan → Thief → Assassin → Blade Dancer → Night Raven, etc., **not alphabetical**),
   Search `TextBox` (substring match on skill name), and a static hint label explaining
   the baseline filter (§4).
2. **Action bar** (`Panel`, 34px): "Key for selected" read-only `TextBox` (key-capture via
   `FormUtils.OnKeyDown`/`OnKeyPress`, same UX as every other key-bind field in the app),
   **Set Key**, **Clear Key**, **Assign Icon...**, **Clear Custom Icon**.
3. **Grid** (`DataGridView`, fills remaining space, `EditMode = EditOnEnter` so a single
   click starts editing Cooldown — no double-click needed, `DoubleBuffered` forced on via
   reflection to stop flicker on rebuild):
   - **Icon** (`DataGridViewImageColumn`, zoomed) — `RTMSkillReference.GetIconForSkill`
     (custom icon override, else the bundled `SKID.png`, else blank).
   - **Class** (read-only)
   - **Skill Name** (read-only)
   - **Type** (read-only — "Supportive"/"Active"/"Passive"/"Class note" from the scrape)
   - **Aimed At** (read-only — "Yourself"/"One ally"/"One enemy"/... from the scrape)
   - **Key** (read-only — reflects the bound `SkillTrigger`, if any; set via the action
     bar, not edited in-grid)
   - **Cooldown (s)** (editable `TextBox` cell — only meaningful once a Buff ID exists;
     see §5)
   - **Buff Duration (s)** (editable `TextBox` cell, optional — same edit rules as
     Cooldown; combined with it, Autobuff derives the "unavailable window" used for its
     backoff retry, see [Skill Capture spec §9](skill-capture.md#9-autobuff-integration-backoff-retry-when-duration-is-set))
   - **Buff ID** (read-only — the `SkillMapEntry.StatusId` once a link exists, blank
     otherwise)
   - **Test** (`DataGridViewButtonColumn`) — sends the bound key via the Skill Capture
     Test mechanism (§5)

Row order: `RTMClassProgression.RankOf(Class)` then skill name (job-tree order, not
alphabetical).

## 3. Data model

This tab does not own any persistence of its own — it is a view and editor over data
owned elsewhere:

| Field shown | Source |
| --- | --- |
| Icon, Class, Skill Name, Type, Aimed At | `RTMSkillReference.All` (read-only, bundled scrape) |
| Key | `ProfileSingleton.GetCurrent().StatusCapture.triggers` (`SkillTrigger`, matched by `SkillId` if non-zero, else by name) |
| Cooldown (s), Buff Duration (s), Buff ID | `SkillMapManager.Entries` (`SkillMapEntry`, matched the same way) |

See [Skill Capture spec §4](skill-capture.md#4-data-model) for the full shape of
`SkillTrigger`/`SkillMapEntry`.

## 4. Filtering rule

A skill row is shown if it passes **both** stages:

1. **Search** (always applies, to every row, linked or not): no text in the Search box,
   or the skill name contains it (substring, case-insensitive). Typing a name is an
   explicit "find this one" request, so an already-linked skill with an unrelated name
   must not clutter those results.
2. **Baseline** — the skill already has a `SkillMapEntry` linked
   (`FindMapEntryFor(s) != null`, **bypasses the rest of this stage**: Class and
   Type/AimedAt), **or** it passes **all** of:
   - `IsSelfBuffCandidate`: `Type == "Supportive"` AND `AimedAt == "Yourself"` (the only
     kind Autobuff can use — it has no concept of targeting, it only ever re-presses a key
     on yourself), **and**
   - Class filter (`"All Classes"` or exact match)

This rule went through three iterations of direct user correction: the first
implementation applied Type/AimedAt as a hard filter with no exception; the user then
asked to not filter out a skill that already has a buff id; a follow-up clarified that an
already-linked skill must bypass **every** filter (Class and Search included); a further
correction then narrowed that back down — bypassing Search too meant a linked skill always
showed up no matter what was typed, drowning out actual search results, so Search was
split out as its own always-applied stage and only Class/Type/AimedAt remain bypassable
for an already-linked skill. The current `RenderGrid()` applies Search as a leading
`.Where(...)` before the linked-bypass `.Where(...)`, rather than folding both into one
conjunction.

## 5. Behavior

### 5.1 Set Key / Clear Key
- **Set Key**: requires a selected row and a captured key; calls
  `StatusCapture.AddTrigger(skill.SkillId ?? 0, skill.Name, key)` — this reuses the exact
  same `SkillTrigger` list Skill Capture's own trigger grid reads/writes, so a key bound
  here shows up there too, and vice versa.
- **Clear Key**: removes the matching trigger via `StatusCapture.RemoveTrigger`.

### 5.2 Test
Clicking a row's Test button (`DataGridViewButtonColumn`) requires an existing trigger for
that skill (prompts to Set Key first if none) and an attached client, then calls the same
`StatusCapture.TestTrigger` used by Skill Capture's "Test Selected" button — sends the key
to the game, diffs the status buffer over a 3-second window, and on exactly one new status
id, writes a `SkillMapEntry` (which is what then populates this row's Buff ID column).

### 5.3 Cooldown / Buff Duration editing
`Grid_CellEndEdit` acts on either the Cooldown or the Buff Duration column (same handler,
branching on which column fired - both share identical validation). If the skill has no
`SkillMapEntry` yet (no Buff ID — i.e. never tested/mapped), the edit is reverted and a
`MessageBox` explains a key must be set and tested first; this cannot be reordered to
"allow setting cooldown/duration before a link exists" without also changing
`SkillMapManager.SetCooldown`/`SetDuration`, which are no-ops if the entry doesn't exist
yet. A blank/non-numeric value clears the field (`0`) rather than erroring, since both are
optional. **Must not** call `RenderGrid()` (which does `Rows.Clear()`) synchronously from
inside this handler — doing so was an actual, previously-hit crash (reentrant grid
teardown mid-cell-commit); the fix was to revert only the one cell directly.

### 5.4 Icon override
**Assign Icon...** opens an `OpenFileDialog` (png/jpg/jpeg/bmp/gif) and calls
`CustomSkillIcons.SetIcon(skill.Key, path)` — re-encodes whatever format was picked to PNG
and stores it next to the executable under `CustomSkillIcons/`, keyed by the RTM
database's stable slug (`skill.Key`, e.g. `"revenant/darkside-shadow"`), not by `SkillId`
(many custom skills share `SkillId == 0`/the same underlying mechanic). **Clear Custom
Icon** removes the override file; the grid then falls back to the bundled `SKID.png` (or
blank if none).

## 6. Cross-cutting / integration

- **Observer wiring**: `Update(ISubject)` re-renders the whole grid on
  `PROFILE_CHANGED`, `SKILL_MAP_CHANGED`, and `SKILL_TRIGGERS_CHANGED` — no polling timer,
  no direct subscription to `SkillMapManager.Changed`/`StatusCapture.TriggersChanged`
  (those are bridged centrally by `Container.cs`; see
  [Skill Capture spec §6](skill-capture.md#6-cross-cutting--integration)).
- Writing a key/cooldown/icon here is indistinguishable, from every other tab's point of
  view, from doing the same thing in Skill Capture or Autobuff — they all converge on the
  same `SkillMapManager`/`StatusCapture.triggers` state and the same notification path.

## 7. Edge cases / validation

- `SelectedSkill()` / row `Tag` is the backing `RTMSkillRef` — equality for
  "is this still the same skill after a rebuild" is `Key` string equality, not reference
  equality, since every render rebuilds all rows from scratch.
- A skill whose `SkillId` is `null`/`0` (most fully-custom skills) is matched to its
  trigger/map entry by exact, case-insensitive name instead — see
  `FindTriggerFor`/`FindMapEntryFor`.
- Grid repaint flicker on rebuild is mitigated by forcing `DoubleBuffered` on via
  reflection (`DataGridView.DoubleBuffered` is `protected`, not settable directly) —
  required because every rebuild repaints every cell, icons included.

## 8. Known limitations

- Type/AimedAt classification is only as accurate as the scraped community database; a
  mis-tagged skill (e.g. actually self-only but tagged otherwise) simply won't appear
  until manually linked once via Skill Capture, after which it becomes permanently
  visible regardless of tagging (§4).
- No bulk actions (e.g. "test all for this class") — every Set Key/Test/cooldown edit is
  one row at a time.
