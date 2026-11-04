using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Item-quality delivery for the "物品品质" research loop (RL_Quality).
    ///
    /// The loop's benefit is a FIXED number of quality tiers (Awful..Legendary): completing it
    /// raises the quality of everything an eligible colonist crafts. Unlike the stat/skill loops,
    /// this cannot hang off a StatDef or a skill, so it is delivered here — the single convergence
    /// point where every recipe-made item gets its quality assigned.
    ///
    /// The effect multiplier does NOT scale the tier count (tiers are discrete and would clamp to
    /// Legendary anyway); it only changes how cheap the research was. That rule lives in
    /// ResearchLoopPlan (costInverseEffectScale), so the tier count read here is always the authored
    /// one regardless of the slider.
    ///
    /// Eligibility (free colonist / slave only) is the same rule every other delivery channel uses,
    /// owned by <see cref="LoopEligibility"/> so it cannot drift.
    /// </summary>
    [HarmonyPatch(typeof(Verse.GenRecipe), "PostProcessProduct")]
    public static class Patch_ItemQuality
    {
        private const string LoopName = "RL_Quality";
        private static int loggedErrors;

        // Positional Harmony args: __0 = product (Thing), __2 = worker (Pawn). Declaring by index
        // avoids depending on the exact parameter names of a 1.6-internal signature.
        public static void Postfix(Thing __0, Pawn __2)
        {
            try
            {
                Thing product = __0;
                Pawn worker = __2;
                if (product == null || worker == null) return;
                if (!LoopEligibility.IsSupported(worker)) return;

                int level = GameComponent_ResearchLoops.GetLevel(LoopName);
                if (level <= 0) return;

                CompQuality comp = product.TryGetComp<CompQuality>();
                if (comp == null) return;

                ResearchLoopTemplate template = ResearchLoopTemplates.Get(LoopName);
                if (template == null) return;

                int tiers = (int)Math.Round(ResearchLoopPlan.EffectValue(template, level));
                if (tiers <= 0) return;

                // Manual tier bump: QualityUtility.AddLevels is internal in 1.6, so we shift the
                // public QualityCategory enum directly and clamp to [Awful, Legendary]. "极差 + 7"
                // lands exactly on Legendary instead of overflowing the enum.
                int raw = (int)comp.Quality + tiers;
                if (raw > (int)QualityCategory.Legendary) raw = (int)QualityCategory.Legendary;
                if (raw < (int)QualityCategory.Awful) raw = (int)QualityCategory.Awful;
                comp.SetQuality((QualityCategory)raw, null);
            }
            catch (Exception ex)
            {
                if (loggedErrors++ == 0)
                {
                    Log.Error("[ResearchLoops] item-quality patch failed: " + ex);
                }
            }
        }
    }
}
