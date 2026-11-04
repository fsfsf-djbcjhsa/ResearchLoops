using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Owns the four ascension hediffs (2026-10-23, user directive: 「把四个飞升效果单拎出来，健康栏中
    /// 独立于循环科技效果」): attach, refresh, strip — the same contract as
    /// <see cref="ResearchLoopBuff"/>, but one hediff per ascension instead of one shared buff.
    ///
    /// The split exists because a player could not tell which tooltip line came from a chain and
    /// which from an ascension: 设计进化's 「全部身体能力 +100%」 sat in the same list as
    /// 「通用劳作速度 ×3.2」. Now:
    ///   - RL_Hediff_TechCycle  → chain lines only (see Hediff_ResearchLoop.BuildTip);
    ///   - RL_Hediff_Ascension_* → that ascension's own lines and stat effects.
    ///
    /// Binding is data: each &lt;Dangerous&gt; element authors `hediffDef=`, and this class resolves
    /// it both ways (def → info for the hediff instance, info → def for the sweep). A missing def
    /// is reported once per name and degrades to "no line" rather than throwing on the hot path.
    ///
    /// Eligibility is <see cref="LoopEligibility.IsLoopTarget"/> — free colonists and slaves, plus
    /// colony mechs once 合成进化 is researched. That last part matters: 设计进化's capacity offset
    /// is now carried by the gene hediff, and a mech must keep receiving it.
    /// </summary>
    public static class AscensionBuff
    {
        private static Dictionary<string, DangerousTechInfo> infoByHediffDef;

        /// <summary>BuffVersion whose capacity cache has already been invalidated (-1 = never).</summary>
        private static int capacityDirtyVersion = -1;

        private static void BuildIndex()
        {
            if (infoByHediffDef != null) return;

            infoByHediffDef = new Dictionary<string, DangerousTechInfo>(System.StringComparer.Ordinal);
            List<DangerousTechInfo> all = ResearchLoopTemplates.DangerousTechs;
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                DangerousTechInfo info = all[i];
                if (info == null || string.IsNullOrEmpty(info.hediffDef)) continue;
                infoByHediffDef[info.hediffDef] = info;
            }
        }

        /// <summary>The ascension an authored hediff belongs to, or null when the def is not ours.</summary>
        public static DangerousTechInfo InfoForDef(HediffDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.defName)) return null;
            BuildIndex();

            DangerousTechInfo info;
            return infoByHediffDef.TryGetValue(def.defName, out info) ? info : null;
        }

        /// <summary>The authored hediff of one ascension, or null when it authors none / the def is missing.</summary>
        public static HediffDef DefFor(DangerousTechInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.hediffDef)) return null;

            // Resolved fresh every call, never cached: a language switch clears and rebuilds every
            // DefDatabase, so a cached HediffDef goes stale and HasHediff would miss the save's
            // instance — double-adding the ascension hediff under the old language's label (the
            // 「循环科技」 duplicate in the health tab, user report 2026-10-25).
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(info.hediffDef);
            if (def == null)
            {
                Log.ErrorOnce("[ResearchLoops] ascension hediff def '" + info.hediffDef +
                              "' is missing (expected Defs/HediffDefs/ResearchLoopAscension.xml); " +
                              "that ascension will have no health-tab line.",
                    info.hediffDef.GetHashCode());
            }
            return def;
        }

        /// <summary>
        /// One sweep over the four authored ascensions: researched ones are given to every eligible
        /// pawn, the rest are taken away. Called from the game component on every research change,
        /// on load, and every 250 ticks — the same cadence as the loop buff, so a pawn who joins,
        /// gets captured or is turned into a mech is corrected within one sweep.
        /// </summary>
        public static void SyncAll()
        {
            List<DangerousTechInfo> all = ResearchLoopTemplates.DangerousTechs;
            if (all == null || all.Count == 0) return;

            int version = GameComponent_ResearchLoops.CurrentBuffVersion;
            bool dirtyCapacities = DangerousTech.GeneAscended && capacityDirtyVersion != version;

            for (int i = 0; i < all.Count; i++)
            {
                DangerousTechInfo info = all[i];
                if (info == null || string.IsNullOrEmpty(info.hediffDef)) continue;

                HediffDef def = DefFor(info);
                if (def == null) continue;

                bool researched = DangerousTech.IsResearched(info);
                // 血肉苦弱 zeroes the fall rates, but a need that is ALREADY low would stay low
                // forever — so the sweep tops the three needs up. Moved here from the loop buff
                // with the split: it is an ascension effect, not a chain effect.
                bool topUpNeeds = info.IsEffect(DangerousTech.EffectCybernetic);
                // 合成进化 zeroes the mech battery's fall rate, but a mech that is ALREADY empty (or
                // one a charger repair just drained) stays down until something lifts the level —
                // so the same sweep tops the battery up. Same reasoning as topUpNeeds above.
                bool topUpEnergy = info.IsEffect(DangerousTech.EffectSynthetic);
                // 设计进化 puts a capacity offset on this stage, and PawnCapacitiesHandler CACHES
                // capacity levels until something dirties them (a hediff add/remove does; a
                // dynamic stage swap does not). Dirty once per BuffVersion, on the gene tech only.
                bool dirtyThis = dirtyCapacities && info.IsEffect(DangerousTech.EffectGene);

                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
                {
                    if (pawn == null || pawn.health == null || pawn.health.hediffSet == null) continue;

                    bool has = pawn.health.hediffSet.HasHediff(def);

                    if (researched && LoopEligibility.IsLoopTarget(pawn))
                    {
                        ResearchLoopBuff.Collapse(pawn, def, true);
                        if (!pawn.health.hediffSet.HasHediff(def)) pawn.health.AddHediff(def);
                        if (topUpNeeds) CyberneticAscension.TopUpNeeds(pawn);
                        if (topUpEnergy) SyntheticAscension.TopUpEnergy(pawn);
                        if (dirtyThis) pawn.health.capacities.Notify_CapacityLevelsDirty();
                    }
                    else if (has)
                    {
                        ResearchLoopBuff.Collapse(pawn, def, false);
                    }
                }
            }

            if (dirtyCapacities) capacityDirtyVersion = version;
        }
    }
}
