using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace _4RTools.Model
{
    // One row from the community-maintained skill database for this server
    // (scraped from its public site - see AGENTS.md for the full story on how
    // "icon" turned out to be the real rAthena skill constant each custom skill
    // is mechanically built on, which is what makes SkillId resolution possible
    // for skills that reuse a stock mechanic).
    public class RTMSkillRef
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Class { get; set; }
        public string SKID { get; set; }
        public int? SkillId { get; set; }

        // "Supportive" / "Active" / "Passive" / "Class note" - the main signal for
        // telling an actual buff apart from an attack or a passive, e.g. so the
        // Skill Bible doesn't suggest testing/triggering something that was never
        // going to leave a status on anyone.
        public string Type { get; set; }

        // Who it can be aimed at when cast ("Yourself", "One ally", "One enemy",
        // "Your party", ...) - lets a supportive skill cast on a target (e.g. a
        // "vampire mask"-style buff placed on an ally) still be recognized as a
        // real buff candidate, just not a self-only one.
        public string AimedAt { get; set; }

        // What the effect actually lands on ("Self", "Buff", "Enemy", "Party", ...).
        public string Target { get; set; }

        public int MaxLevel { get; set; }
    }

    // Read-only lookup over Resources/RTMSkills/skills-reference.json + the bundled
    // icon set, both shipped as Content (CopyToOutputDirectory) rather than compiled
    // resx resources, since this is a complete-but-separate dataset scraped from a
    // third-party community site, not hand-picked app resources.
    public static class RTMSkillReference
    {
        private static readonly string DataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "RTMSkills", "skills-reference.json");
        private static readonly string IconDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "RTMSkills", "icons");

        private static List<RTMSkillRef> all;
        private static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        static RTMSkillReference()
        {
            Load();
        }

        public static IReadOnlyList<RTMSkillRef> All => all;

        private static void Load()
        {
            all = new List<RTMSkillRef>();
            try
            {
                if (File.Exists(DataPath))
                {
                    string json = File.ReadAllText(DataPath);
                    all = JsonConvert.DeserializeObject<List<RTMSkillRef>>(json) ?? new List<RTMSkillRef>();
                }
            }
            catch
            {
                // Missing/corrupt bundled data: behave as if the reference simply isn't
                // available (name/ID search and icons just won't offer suggestions).
            }
        }

        public static List<RTMSkillRef> Search(string query, int limit = 25)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<RTMSkillRef>();

            return all
                .Where(s => s.Name != null && s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.Name)
                .Take(limit)
                .ToList();
        }

        public static Bitmap GetIconBySkillId(int skillId)
        {
            if (skillId == 0) return null;

            RTMSkillRef match = all.FirstOrDefault(s => s.SkillId == skillId);
            return match == null ? null : GetIconBySkid(match.SKID);
        }

        // User-assigned icon (CustomSkillIcons, keyed by the stable RTM "Key" slug)
        // takes priority over the bundled one, since that's the whole point of
        // letting someone override a missing/wrong icon for a specific skill entry.
        public static Bitmap GetIconForSkill(RTMSkillRef skill)
        {
            if (skill == null) return null;

            Bitmap custom = CustomSkillIcons.GetIcon(skill.Key);
            if (custom != null) return custom;

            return GetIconBySkid(skill.SKID);
        }

        public static Bitmap GetIconBySkid(string skid)
        {
            if (string.IsNullOrEmpty(skid)) return null;

            if (iconCache.TryGetValue(skid, out Bitmap cached)) return cached;

            string path = Path.Combine(IconDir, skid + ".png");
            if (!File.Exists(path)) return null;

            try
            {
                Bitmap bmp = new Bitmap(path);
                iconCache[skid] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
