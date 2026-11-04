using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Pins this mod's research page to the second slot of the tab strip.
    ///
    /// 2026-11-01 (user report "顶部根本没有「循环科技」页签", while the log said "29 loop defs in tab
    /// ResearchLoops"). Read from the decompiled 1.6 UI, not guessed:
    ///   - MainTabWindow_Research.PostOpen builds one button per
    ///     DefDatabase&lt;ResearchTabDef&gt;.AllDefs entry, in def order;
    ///   - TabDrawer.DrawTabsOverflow then splits that list into ceil(count * 100 / width) rows and
    ///     draws every one of them — it hides nothing, but it never reorders either.
    /// A 400-mod list carries 33 research tabs, so the page landed in row two or three, its label
    /// truncated to fit a 100-200px button, and it read as "the tab does not exist". There is no
    /// XML field for tab order, so this list is the only lever.
    ///
    /// ZERO cost for normal users: when the page already sits in the first two slots the list is
    /// left alone. The one-time log line exists so the next report of this can be answered from
    /// Player.log instead of a screenshot — it states the tab count and where the page was found.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), "PostOpen")]
    public static class Patch_ResearchTabOrder
    {
        private static FieldInfo tabsField;
        private static FieldInfo defField;
        private static bool reported;

        public static void Postfix(MainTabWindow_Research __instance)
        {
            if (__instance == null) return;

            if (tabsField == null)
            {
                tabsField = AccessTools.Field(typeof(MainTabWindow_Research), "tabs");
            }
            if (tabsField == null) return;

            IList tabs = tabsField.GetValue(__instance) as IList;
            if (tabs == null || tabs.Count == 0) return;

            int index = -1;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (DefNameOf(tabs[i]) == ResearchLoopsIds.TabDefName)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                if (!reported)
                {
                    reported = true;
                    Log.Error("[ResearchLoops] the research page is not in the tab strip (" + tabs.Count +
                              " tab(s) present). The loop defs were generated but nothing can reach them — " +
                              "check that Defs/ResearchTabDefs/ResearchLoopsTab.xml loaded.");
                }
                return;
            }

            if (!reported)
            {
                reported = true;
                Log.Message("[ResearchLoops] research page is tab " + (index + 1) + " of " + tabs.Count +
                            " on this save.");
            }

            // Slot 0 is the vanilla Main page; putting ours next to it keeps it in the first row no
            // matter how many tabs other mods add.
            if (index <= 1) return;

            object record = tabs[index];
            tabs.RemoveAt(index);
            tabs.Insert(1, record);
        }

        /// <summary>
        /// The record type is private (MainTabWindow_Research.ResearchTabRecord), so its def is read
        /// by reflection. That also keeps this patch compiling against the public API only.
        /// </summary>
        private static string DefNameOf(object record)
        {
            if (record == null) return null;
            if (defField == null)
            {
                defField = AccessTools.Field(record.GetType(), "def");
            }
            if (defField == null) return null;

            ResearchTabDef def = defField.GetValue(record) as ResearchTabDef;
            return def == null ? null : def.defName;
        }
    }
}
