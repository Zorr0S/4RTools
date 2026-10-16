using System;
using System.Collections.Generic;

namespace _4RTools.Model
{
    // The actual job-change tree for this server (from its "Classes" page's
    // prerequisites - e.g. Revenant requires "Trickster Job Level 70"), used to
    // sort classes by progression (Orphan -> Trickster -> Revenant) instead of
    // alphabetically. The scraped skill database's own "classes" list has no
    // such order - it's alphabetical - so this is hand-derived from the site
    // separately and kept here rather than in the scraped skill JSON.
    public static class RTMClassProgression
    {
        private static readonly Dictionary<string, string[]> Tree = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orphan"] = new[] { "Thief", "Trickster", "Vagabond", "Merchant", "Bouncer", "Dark Knight", "Illusionist", "Jester", "Judge", "Mimic", "Peacekeeper", "Raider", "Ronin" },
            ["Thief"] = new[] { "Assassin", "Prowler", "Rogue", "Shadowseer", "Unchained Thief" },
            ["Assassin"] = new[] { "Blade Dancer", "Executioner", "Scoundrel", "Shinobi", "Unchained Assassin" },
            ["Blade Dancer"] = new[] { "Night Raven" },
            ["Executioner"] = new[] { "Gravekeeper" },
            ["Scoundrel"] = new[] { "Black Plague" },
            ["Shinobi"] = new[] { "Satsujin" },
            ["Rogue"] = new[] { "Deadeye", "Duelist", "Mystic", "Saboteur", "Unchained Rogue" },
            ["Deadeye"] = new[] { "Sinner" },
            ["Duelist"] = new[] { "Kingslayer" },
            ["Mystic"] = new[] { "Arcane Master" },
            ["Saboteur"] = new[] { "Blast Juggler" },
            ["Unchained Thief"] = new[] { "Phantom Thief" },
            ["Trickster"] = new[] { "Revenant" },
            ["Vagabond"] = new[] { "Legend" },
            ["Bouncer"] = new[] { "Pit Boss" },
            ["Raider"] = new[] { "Dracomancer" },
        };

        private static readonly Dictionary<string, int> rank = BuildRanks();

        private static Dictionary<string, int> BuildRanks()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int counter = 0;

            void Visit(string className)
            {
                if (result.ContainsKey(className)) return;
                result[className] = counter++;

                if (Tree.TryGetValue(className, out string[] children))
                {
                    foreach (string child in children) Visit(child);
                }
            }

            Visit("Orphan");
            return result;
        }

        // Depth-first position in the job tree (Orphan=0, then its whole branch
        // before the next one) - lower sorts earlier. Unknown/unlisted classes
        // (shouldn't happen for this server, but just in case) sort last.
        public static int RankOf(string className)
        {
            if (string.IsNullOrEmpty(className)) return int.MaxValue;
            return rank.TryGetValue(className, out int r) ? r : int.MaxValue;
        }
    }
}
