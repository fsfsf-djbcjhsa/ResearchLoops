using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Projects a loop's level state onto its single ResearchProjectDef: baseCost (the cost
    /// of the level being offered), label and description. Called from every place that
    /// changes or restores the counter, so def state and counter cannot drift apart:
    ///   - the ResearchManager.FinishProject postfix (a level was just completed),
    ///   - GameComponent_ResearchLoops on game load and on new game — defs are static per
    ///     process, so a def levelled up in a previous game must be reset when a game with
    ///     a different (or empty) counter is loaded/started.
    ///
    /// All numbers and texts come from ResearchLoopPlan (pure). This class only writes def
    /// state, and never touches vanilla research bookkeeping: raw progress stays in
    /// ResearchManager.progress and is offset on read by the ProgressReal postfix.
    /// </summary>
    public static class ResearchLoopProgress
    {
        /// <summary>Cached reflection handle for the private tooltip cache (see ClearCachedDescription).</summary>
        private static FieldInfo cachedDescriptionField;

        /// <summary>Set once the field-lookup has already been reported, so a game update logs once, not per level-up.</summary>
        private static bool cachedDescriptionFieldMissing;

        /// <summary>True when the def is one of this mod's loop defs.</summary>
        public static bool IsLoop(ResearchProjectDef def)
        {
            ResearchLoopExtension ext = ExtensionOf(def);
            return ext != null && ext.IsLoopDef;
        }

        /// <summary>The loop this def belongs to, or null for any vanilla/foreign def.</summary>
        public static ResearchLoopTemplate TemplateOf(ResearchProjectDef def)
        {
            ResearchLoopExtension ext = ExtensionOf(def);
            if (ext == null || !ext.IsLoopDef) return null;
            return ResearchLoopTemplates.Get(ext.loop);
        }

        /// <summary>
        /// The name an ascension node quotes for one prerequisite chain, in the active language.
        /// Null when the defName is not an authored loop — ResearchLoopPlan.AscensionGateLine then
        /// prints the raw defName, so a typo in `prerequisite` shows up on the node instead of
        /// quietly naming nothing.
        /// </summary>
        public static string PrereqDisplayName(string defName)
        {
            ResearchLoopTemplate template = ResearchLoopTemplates.Get(defName);
            if (template == null) return null;
            return ResearchLoopPlan.Pick(template.name, template.nameEn);
        }

        /// <summary>Sets baseCost/label/description to the state for 'completedLevels'.</summary>
        public static void Apply(ResearchProjectDef def, int completedLevels)
        {
            ResearchLoopTemplate template = TemplateOf(def);
            if (template == null) return;

            // Keep the label/description language in step with the active language. Cheap: one
            // string compare, and Apply is already gated by the game component's version counter.
            Localization.Sync();

            def.baseCost = ResearchLoopPlan.CostForLevel(template, completedLevels,
                ResearchLoopsConfig.CostScale, ResearchLoopsConfig.BonusScale);
            def.label = ResearchLoopPlan.Label(template, completedLevels);
            // A researched dangerous tech zeroes the loop's coefficient, and the node must quote
            // the number the game actually applies — hence the flag on the description too.
            def.description = ResearchLoopPlan.Description(template, completedLevels,
                ResearchLoopsConfig.BonusScale, DangerousTech.IsZeroed(template));

            def.ClearCachedData();
            ClearCachedDescription(def);
        }

        /// <summary>
        /// Re-applies every loop def from the given counter. Cheap (36 defs) and idempotent,
        /// so both load and new-game paths can call it without coordination. Dangerous-tech defs
        /// are re-projected here as well (see <see cref="RefreshDangerousCosts"/> and
        /// DangerousTech.RefreshPlacement).
        /// </summary>
        public static void ApplyAll(GameComponent_ResearchLoops component)
        {
            // Pick the language up front so the standalone dangerous text below (which does not go
            // through Apply) reads the same flag as the loop defs.
            Localization.Sync();

            List<ResearchProjectDef> defs = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ResearchLoopExtension ext = ExtensionOf(defs[i]);
                if (ext == null || !ext.IsLoopDef) continue;

                int completed = component != null ? component.GetCount(ext.loop) : 0;
                Apply(defs[i], completed);
            }

            RefreshDangerousCosts();
            RefreshDangerousText();
            DangerousTech.RefreshPlacement();
        }

        /// <summary>
        /// Re-picks the four standalone ascension defs' label/description in the ACTIVE language.
        /// The load-time generator runs before the language DB is finalized (Player.log shows
        /// "generated ... defs" before "updating labels for simplified chinese"), so those two strings
        /// can be baked in English. Loop defs self-heal through Apply(); the ascension defs had no
        /// such refresh, which showed 心胜于物 in English on a Chinese game (user report 2026-10-25).
        /// label/description are display-only, so refreshing a FINISHED ascension is safe — unlike
        /// baseCost, they do not participate in IsFinished.
        /// </summary>
        private static void RefreshDangerousText()
        {
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                if (info == null) continue;

                ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(info.defName);
                if (def == null) continue;

                def.label = ResearchLoopPlan.Pick(info.label, info.labelEn);
                def.description = ResearchLoopPlan.StandaloneNodeText(info, PrereqDisplayName);
                def.ClearCachedData();
                ClearCachedDescription(def);
            }
        }

        /// <summary>
        /// Dangerous-tech costs follow the cost slider while the project is still researchable.
        /// A COMPLETED one keeps the cost it was finished at on purpose: vanilla's IsFinished
        /// compares stored progress against baseCost, so re-projecting a finished project would
        /// silently un-finish it — and with it the zeroed coefficient (or the ascension).
        /// </summary>
        private static void RefreshDangerousCosts()
        {
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.dangerous == null) continue;

                RefreshCost(DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                    DangerousTech.DefNameFor(template)), template.dangerous.cost);
            }

            // Standalone dangerous techs (心胜于物) are priced by the same slider.
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                if (info == null) continue;
                RefreshCost(DefDatabase<ResearchProjectDef>.GetNamedSilentFail(info.defName), info.cost);
            }
        }

        /// <summary>Writes one dangerous tech's scaled cost, leaving a finished project alone.</summary>
        private static void RefreshCost(ResearchProjectDef def, float authoredCost)
        {
            if (def == null || def.IsFinished) return;

            float scale = LoopMath.Safe(ResearchLoopsConfig.CostScale, 1f);
            if (scale <= 0f) scale = 1f;
            def.baseCost = authoredCost * scale;
            def.ClearCachedData();
            ClearCachedDescription(def);
        }

        private static ResearchLoopExtension ExtensionOf(ResearchProjectDef def)
        {
            if (def == null) return null;
            return def.GetModExtension<ResearchLoopExtension>();
        }

        /// <summary>
        /// ResearchProjectDef caches GetTip()'s text in a private [Unsaved] field that
        /// Def.ClearCachedData() does not know about; without this the research-node tooltip
        /// would keep showing the first level's text forever after a level-up. A game update
        /// that renames the field is a red test, not silent staleness: Tests/Verify.cs
        /// asserts the field still exists via Mono.Cecil.
        /// </summary>
        public static void ClearCachedDescription(ResearchProjectDef def)
        {
            if (cachedDescriptionFieldMissing) return;

            if (cachedDescriptionField == null)
            {
                cachedDescriptionField = typeof(ResearchProjectDef).GetField("cachedDescription",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (cachedDescriptionField == null)
                {
                    // Report once: a rename would otherwise spam one error per level-up for all
                    // 36 chains, drowning the log in thousands of identical lines.
                    cachedDescriptionFieldMissing = true;
                    Log.Error("[ResearchLoops] ResearchProjectDef.cachedDescription not found; research " +
                              "tooltips may show a stale level after level-up. (reported once)");
                    return;
                }
            }
            cachedDescriptionField.SetValue(def, null);
        }
    }
}
