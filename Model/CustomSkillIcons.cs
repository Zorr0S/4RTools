using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace _4RTools.Model
{
    // Lets the user assign their own icon for a scraped RTM skill that has none
    // (the 8 or so fully-custom skills with no stock equivalent - see
    // RTMSkillReference) or wants to override a wrong/ugly bundled one. Stored
    // next to the executable (not under Resources\RTMSkills, which is a Content
    // item that ships with/gets overwritten by the app build) so it survives
    // updates, keyed by the RTM database's stable "Key" slug (e.g.
    // "revenant/darkside-shadow") since SkillId is 0/shared for custom skills.
    public static class CustomSkillIcons
    {
        private static readonly string FolderName = "CustomSkillIcons";
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private static string PathFor(string skillKey)
        {
            string safeName = string.Join("_", skillKey.Split(Path.GetInvalidFileNameChars().Concat(new[] { '/' }).ToArray()));
            return Path.Combine(FolderName, safeName + ".png");
        }

        public static void SetIcon(string skillKey, string sourceImagePath)
        {
            Directory.CreateDirectory(FolderName);
            string destPath = PathFor(skillKey);

            // Re-encode as PNG rather than a raw file copy, so whatever format the
            // user picked (jpg, bmp, ...) still loads back cleanly as one.
            using (Image img = Image.FromFile(sourceImagePath))
            {
                img.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            cache.Remove(skillKey);
        }

        public static void RemoveIcon(string skillKey)
        {
            string path = PathFor(skillKey);
            if (File.Exists(path)) File.Delete(path);
            cache.Remove(skillKey);
        }

        public static bool HasCustomIcon(string skillKey)
        {
            return File.Exists(PathFor(skillKey));
        }

        public static Bitmap GetIcon(string skillKey)
        {
            if (string.IsNullOrEmpty(skillKey)) return null;
            if (cache.TryGetValue(skillKey, out Bitmap cached)) return cached;

            string path = PathFor(skillKey);
            if (!File.Exists(path)) return null;

            try
            {
                Bitmap bmp = new Bitmap(path);
                cache[skillKey] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
