using System;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Language switch for every string this mod renders itself (2026-10-23, user directive
    /// 「加入英文版，除语言为中文外其余语言对应均为英文」).
    ///
    /// Rule: the active language's name contains "Chinese" (simplified OR traditional) → Chinese;
    /// every other language → English. There is deliberately no third translation.
    ///
    /// Why a flag instead of reading the language at every call site: ResearchLoopPlan and
    /// ResearchLoopTemplates are compiled by Tests/Verify without RimWorld, so they cannot touch
    /// LanguageDatabase. They expose <see cref="ResearchLoopPlan.English"/> and a Pick() helper
    /// instead; every Verse-side entry point calls <see cref="Sync"/> first, which is cheap (one
    /// string compare) and makes the flag correct for the frame that is being drawn.
    ///
    /// Strings that live in Defs XML (hediff labels, the research tab, the gene) are translated the
    /// normal RimWorld way — Languages/English/DefInjected — because those defs are loaded, not
    /// generated.
    /// </summary>
    public static class Localization
    {
        /// <summary>True when the active language is Chinese (simplified or traditional).</summary>
        public static bool IsChinese
        {
            get
            {
                // Prefer Prefs.LangFolderName: LanguageDatabase.SelectLanguage writes it
                // synchronously at the very start, whereas activeLanguage is only updated later
                // inside the deferred LoadAllPlayData long event. Reading activeLanguage from a
                // LanguageDatabase.SelectLanguage postfix would see the OLD language and re-pick
                // every generated node into the wrong tongue (2026-10-25 regression: a Chinese
                // game showed English loop names after a language switch).
                string name = Prefs.LangFolderName;
                if (string.IsNullOrEmpty(name))
                {
                    LoadedLanguage lang = LanguageDatabase.activeLanguage;
                    name = lang?.folderName;
                }
                return !string.IsNullOrEmpty(name) &&
                       name.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>Pushes the current language into the Verse-free layers.</summary>
        public static void Sync()
        {
            bool english = !IsChinese;
            ResearchLoopPlan.English = english;
            LoopMath.English = english;
        }

        /// <summary>Chinese when the active language is Chinese, English otherwise.</summary>
        public static string T(string chinese, string english)
        {
            return IsChinese ? chinese : english;
        }
    }
}
