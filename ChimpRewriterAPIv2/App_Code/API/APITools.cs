using System;
using System.Collections.Generic;

namespace ChimpRewriterAPIv3.API
{
    public static class APITools
    {
        public static bool CustomBoolParse(string text, bool defaultValue)
        {
            bool result = string.IsNullOrEmpty(text) ? defaultValue : text.Equals("1");
            return result;
        }

        public static int CustomIntParse(string text, int defaultValue)
        {
            if (string.IsNullOrEmpty(text)) return defaultValue;
            if (text.ToLower() == "false") return 0;
            if (text.ToLower() == "true") return 1;
            int result;
            if (!int.TryParse(text, out result))
                result = defaultValue;
            return result;
        }

        public static void ReplaceProtectTags(ref string text, string tags, out List<string[]> taglist)
        {
            taglist = new List<string[]>();
            if (string.IsNullOrEmpty(tags)) return;
            //Get list of tag replacements
            string[] tokens = tags.Split(new char[] { ',' });
            foreach (var t in tokens)
            {
                string[] chars = t.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (chars.Length != 2) continue;
                taglist.Add(new string[] { chars[0], chars[1] });
            }
            foreach (var t in taglist)
            {
                //replace tag start
                text = text.Replace(t[0], "##" + t[0]);
                //replace tag end
                text = text.Replace(t[1], t[1] + "##");
            }
        }
        public static void ReturnProtectTags(ref string text, List<string[]> taglist)
        {
            foreach (var t in taglist)
            {
                //replace tag start
                text = text.Replace("##" + t[0], t[0]);
                //replace tag end
                text = text.Replace(t[1] + "##", t[1]);
            }
        }
    }
}
