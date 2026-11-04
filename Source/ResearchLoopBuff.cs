using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Owns the one hediff that makes every buff-delivered loop visible: attach, refresh, strip.
    /// Same contract and eligibility as <see cref="ResearchLoopGene"/> — the sweep is driven from
    /// the game component on every leveled-up loop, on load, and every 250 ticks (see DESIGN §15).
    ///
    /// The buff is added only to pawns the mod is allowed to buff (LoopEligibility), and only
    /// while at least one buff- or patch-delivered chain has a completed level — at level 0
    /// there is nothing to report, exactly like the old StatPart's empty tooltip.
    /// </summary>
    public static class ResearchLoopBuff
    {
        public const string HediffDefName = "RL_Hediff_TechCycle";

        private static bool defUnavailable;

        public static HediffDef Def
        {
            get
            {
                // NEVER cache the def object. An in-session language switch runs
                // PlayDataLoader.ClearAllPlayData, which clears EVERY DefDatabase and rebuilds it
                // — so a cached reference is stale the moment the player changes language, and
                // every pointer-equality check against it (HediffSet.HasHediff, Collapse) then
                // silently fails. That is exactly how the buff came to be listed TWICE in the
                // health tab after a switch to English: the stale def (label 「循环科技」, resolved
                // during the Chinese session) received a second instance next to the save's fresh
                // one ("Research Loop") — user report 2026-10-25. GetNamedSilentFail is a
                // dictionary lookup, so resolving it fresh costs nothing measurable.
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(HediffDefName);
                if (def == null && !defUnavailable)
                {
                    defUnavailable = true;
                    Log.Error("[ResearchLoops] hediff def '" + HediffDefName + "' is missing " +
                              "(expected Defs/HediffDefs/ResearchLoopBuff.xml); the loop stat " +
                              "bonuses would be silently missing and invisible.");
                }
                return def;
            }
        }

        /// <summary>
        /// True when at least one loop the buff can report has a completed level. Skill loops
        /// don't count — they are carried by the gene — but patch loops do (they have no stat to
        /// ride, yet still deserve a visible line).
        ///
        /// Ascensions do NOT count here any more (2026-10-23 split): each one has its own hediff
        /// (AscensionBuff / Hediff_Ascension), so this buff exists exactly while some chain is
        /// researched. A colony whose loop counters were somehow reset now simply loses the buff —
        /// the ascension effects survive on their own hediffs, which is the point of the split.
        /// </summary>
        public static bool AnyActive()
        {
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null) continue;
                if (template.delivery != "buff" && template.delivery != "patch") continue;
                if (GameComponent_ResearchLoops.GetLevel(template.defName) > 0) return true;
            }
            return false;
        }

        public static bool HasHediff(Pawn pawn)
        {
            HediffDef def = Def;
            if (pawn == null || def == null || pawn.health == null || pawn.health.hediffSet == null) return false;
            return pawn.health.hediffSet.HasHediff(def);
        }

        public static void Ensure(Pawn pawn)
        {
            HediffDef def = Def;
            if (pawn == null || def == null) return;
            if (!LoopEligibility.IsLoopTarget(pawn)) return;
            if (pawn.health == null || pawn.health.hediffSet == null) return;

            Collapse(pawn, def, true);
            if (!pawn.health.hediffSet.HasHediff(def)) pawn.health.AddHediff(def);
        }

        /// <summary>
        /// Removes extra instances of one hediff def — keeping the first when <paramref name="keepOne"/>
        /// is true, removing every one when false. The sweep used to only ever ADD when absent and STRIP
        /// a single instance when ineligible, so a second instance was never cleaned up and the health
        /// tab listed the buff twice — user report 2026-10-25.
        ///
        /// Matching is by defName, NOT by object identity: a language switch rebuilds every def
        /// object, so an instance still pointing at the pre-rebuild object carries the OLD
        /// language's label and must still be recognised here. When keeping one, the instance on
        /// the CURRENT def object wins over a stale-labelled one, so the survivor always renders
        /// in the active language. Idempotent; called from both buff sweeps so loop and ascension
        /// hediffs both self-heal.
        /// </summary>
        public static void Collapse(Pawn pawn, HediffDef def, bool keepOne)
        {
            if (pawn == null || def == null || pawn.health == null || pawn.health.hediffSet == null) return;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            Hediff keep = null;
            if (keepOne)
            {
                for (int i = 0; i < hediffs.Count; i++)
                {
                    Hediff h = hediffs[i];
                    if (h == null || !SameDef(h.def, def)) continue;
                    if (h.def == def) { keep = h; break; } // the current object wins outright
                    if (keep == null) keep = h;            // else remember the first match
                }
            }

            List<Hediff> remove = null;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h == null || !SameDef(h.def, def)) continue;
                if (keepOne && h == keep) continue;
                if (remove == null) remove = new List<Hediff>();
                remove.Add(h);
            }
            if (remove == null) return;

            for (int i = 0; i < remove.Count; i++) pawn.health.RemoveHediff(remove[i]);
            Log.Warning("[ResearchLoops] removed " + remove.Count + " duplicate '" + def.defName +
                        "' hediff(s) from " + (pawn.LabelShort ?? "a pawn"));
        }

        /// <summary>
        /// True when both references are the same def object, or two objects sharing one defName —
        /// the pre/post language-rebuild pair. Object identity alone would let a stale instance
        /// survive the sweep (and keep rendering the old language).
        /// </summary>
        private static bool SameDef(HediffDef a, HediffDef b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.defName, b.defName, StringComparison.Ordinal);
        }

        public static void Strip(Pawn pawn)
        {
            HediffDef def = Def;
            if (pawn == null || def == null) return;
            if (pawn.health == null || pawn.health.hediffSet == null) return;
            if (!pawn.health.hediffSet.HasHediff(def)) return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null) pawn.health.RemoveHediff(existing);
        }

        /// <summary>
        /// One sweep: give the buff to every eligible pawn while any chain is researched, and
        /// take it from everyone else (prisoners, guests, animals, or a colony between levels).
        /// Since 合成进化 the eligible set includes the colony's own mechs
        /// (<see cref="LoopEligibility.IsLoopTarget"/>), which is how 机械体吃到循环科技加成 works.
        /// </summary>
        public static void SyncAll()
        {
            HediffDef def = Def;
            if (def == null) return;

            bool active = AnyActive();

            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (pawn == null || pawn.health == null || pawn.health.hediffSet == null) continue;

                if (active && LoopEligibility.IsLoopTarget(pawn))
                {
                    Collapse(pawn, def, true);
                    if (!pawn.health.hediffSet.HasHediff(def)) pawn.health.AddHediff(def);
                }
                else if (pawn.health.hediffSet.HasHediff(def))
                {
                    // Strip EVERY instance, not just the first, so an ineligible pawn cannot keep a
                    // stale duplicate around.
                    Collapse(pawn, def, false);
                }
            }
        }
    }
}
