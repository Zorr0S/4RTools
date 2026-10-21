using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace _4RTools.Model
{
    // One entry of a user-built or imported status-id -> skill mapping.
    // The whole point of this map is EffectStatusID -> SkillID: the status id is what
    // the client's status buffer gives us, SkillId is the actual RO skill id (as used in
    // skill_db/skill_id) that caused it - SkillName is just a human-readable label for
    // whichever skill id, since most players know a skill by name, not by number.
    // See SkillMapManager for persistence; see Model/StatusCapture.cs for how
    // status ids are discovered in the first place.
    public class SkillMapEntry
    {
        public uint StatusId { get; set; }
        public int SkillId { get; set; }
        public string SkillName { get; set; }
        public string Note { get; set; } = "";

        // 0 = not configured. When set, AutoBuff won't retry this status's key
        // until this many seconds have passed since it last pressed it - some
        // skills have a longer real cooldown than their buff duration (e.g. True
        // Sight: 80s cooldown, shorter duration), which otherwise makes AutoBuff
        // spam the key uselessly for the gap between expiry and availability.
        public int CooldownSeconds { get; set; }

        // Optional. How long the buff itself lasts once cast. Combined with
        // CooldownSeconds this gives the "unavailable window" (Cooldown - Duration):
        // the gap after the buff naturally expires where it genuinely can't be
        // recast yet. AutoBuff uses this as a starting retry interval instead of
        // blindly trusting the original cast time for the whole CooldownSeconds -
        // if the buff disappears early (e.g. death clears it mid-duration), the
        // original cast-time countdown is now wrong, so AutoBuff probes on a
        // backoff schedule (starting at this gap, halving each miss down to 3s)
        // rather than waiting out a timer that no longer reflects reality.
        public int DurationSeconds { get; set; }
    }

    // Persists next to the executable, same convention as supported_servers.json
    // (see LocalServerManager) - this is independent of any single Profile because
    // it describes the client/server's skills, not a user's automation settings.
    public static class SkillMapManager
    {
        private static readonly string FileName = "SkillMap.json";

        // Bundled starter map (built from this server's own skill_db.yml plus prior
        // Skill Capture sessions - see docs/specs/skill-capture.md) so a fresh
        // install doesn't start from zero. Shipped as Content under Resources/, same
        // convention as Resources/RTMSkills.
        private static readonly string SeedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "SkillMap.seed.json");

        private static Dictionary<uint, SkillMapEntry> entries;

        static SkillMapManager()
        {
            Load();
        }

        public static IReadOnlyDictionary<uint, SkillMapEntry> Entries => entries;

        // Fired after any write (including from a background thread - e.g. a Skill
        // Trigger's watcher landing a result a few seconds after Test/auto-detect).
        // Subscribers must marshal back to their own UI thread themselves. This
        // replaces what used to be "just poll the grid on a timer every second" in
        // StatusCaptureForm/SkillBibleForm - that caused real bugs (a grid rebuild
        // mid-edit could crash, interrupted double-clicks, visible flicker) for data
        // that actually only ever changes on an explicit write, never continuously.
        public static event System.Action Changed;

        public static SkillMapEntry Get(uint statusId)
        {
            return entries.TryGetValue(statusId, out SkillMapEntry entry) ? entry : null;
        }

        public static void SetSkill(uint statusId, int skillId, string skillName, string note = "")
        {
            int existingCooldown = entries.TryGetValue(statusId, out SkillMapEntry old) ? old.CooldownSeconds : 0;
            entries[statusId] = new SkillMapEntry { StatusId = statusId, SkillId = skillId, SkillName = skillName, Note = note ?? "", CooldownSeconds = existingCooldown };
            Save();
            Changed?.Invoke();
        }

        public static void SetCooldown(uint statusId, int cooldownSeconds)
        {
            if (entries.TryGetValue(statusId, out SkillMapEntry entry))
            {
                entry.CooldownSeconds = cooldownSeconds;
                Save();
                Changed?.Invoke();
            }
        }

        public static void SetDuration(uint statusId, int durationSeconds)
        {
            if (entries.TryGetValue(statusId, out SkillMapEntry entry))
            {
                entry.DurationSeconds = durationSeconds;
                Save();
                Changed?.Invoke();
            }
        }

        public static void Remove(uint statusId)
        {
            if (entries.Remove(statusId))
            {
                Save();
                Changed?.Invoke();
            }
        }

        public static void Load()
        {
            entries = new Dictionary<uint, SkillMapEntry>();

            // Fresh install: copy the bundled starter map in as the real file, so it
            // becomes the user's own editable copy from here on. Never touches an
            // existing file, even an empty one someone cleared on purpose.
            if (!File.Exists(FileName))
            {
                try
                {
                    if (File.Exists(SeedFilePath))
                    {
                        File.Copy(SeedFilePath, FileName);
                    }
                }
                catch
                {
                    // Couldn't seed (read-only folder, missing bundle, ...) - start empty.
                }
            }

            if (!File.Exists(FileName)) return;

            try
            {
                string json = File.ReadAllText(FileName);
                List<SkillMapEntry> list = JsonConvert.DeserializeObject<List<SkillMapEntry>>(json) ?? new List<SkillMapEntry>();
                foreach (SkillMapEntry entry in list)
                {
                    entries[entry.StatusId] = entry;
                }
            }
            catch
            {
                // Corrupt file: start empty rather than crashing the app on load.
            }
        }

        private static void Save()
        {
            string json = JsonConvert.SerializeObject(new List<SkillMapEntry>(entries.Values), Formatting.Indented);
            File.WriteAllText(FileName, json);
        }

        // Merges a shared/community JSON export into the current map.
        // overwrite = false keeps any name the user already assigned locally.
        // Returns how many entries were actually applied.
        public static int Import(string filePath, bool overwrite)
        {
            string json = File.ReadAllText(filePath);
            List<SkillMapEntry> imported = JsonConvert.DeserializeObject<List<SkillMapEntry>>(json) ?? new List<SkillMapEntry>();
            int applied = 0;

            foreach (SkillMapEntry entry in imported)
            {
                if (overwrite || !entries.ContainsKey(entry.StatusId))
                {
                    entries[entry.StatusId] = entry;
                    applied++;
                }
            }

            Save();
            Changed?.Invoke();
            return applied;
        }

        public static void Export(string filePath)
        {
            string json = JsonConvert.SerializeObject(new List<SkillMapEntry>(entries.Values), Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
    }
}
