# Specs

Detailed per-tab specifications for the three tabs added during the "read skill data from
the client" investigation (see `AGENTS.md` for how that investigation led here). These go
deeper than the one-section-per-feature summaries in [Features](../features.md): UI layout
control-by-control, exact data model, step-by-step behavior, and known edge
cases/limitations — written for anyone extending these tabs without having to re-derive
intent from the code alone.

| Tab | Spec | One-line summary |
| --- | --- | --- |
| Skill Capture | [skill-capture.md](skill-capture.md) | Passively logs every status id seen on the client, and lets a user map `StatusId -> SkillId` by hand or by watching a hotkey's effect. |
| Skill Bible | [skill-bible.md](skill-bible.md) | Browsable, filterable table over the full scraped skill database for this server; a curated alternate editor over the same map Skill Capture writes. |
| Mouse Remap | [mouse-remap.md](mouse-remap.md) | Binds a keyboard key to each mouse side button (XButton1/XButton2), replacing a standalone AutoHotkey script. |

Other existing features are documented in [Features](../features.md) instead; this folder
is specifically for tabs that warranted a deeper spec than that format's section-per-feature
layout.
