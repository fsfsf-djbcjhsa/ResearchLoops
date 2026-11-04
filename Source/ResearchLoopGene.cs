using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The visible carrier of the skill bonuses: one gene, 「科技循环」, on every free colonist and
    /// slave.
    ///
    /// The bonus is applied at read time (Patch_SkillLevel), which the UI never shows. A gene is
    /// the one vanilla mechanism that both sits on every pawn and feeds
    /// <see cref="SkillRecord.Aptitude"/>, which the skill panel already renders — so the gene's
    /// aptitude list is rewritten to match the colony's completed levels and the numbers become
    /// visible in the gene card and the skill tooltip.
    ///
    /// Two things this must not do:
    ///   - touch pawns the mod may not buff (LoopEligibility), and
    ///   - leave a stale aptitude behind: SkillRecord caches Aptitude, so every change has to call
    ///     DirtyAptitudes() or the panel keeps showing the old number until something else
    ///     invalidates it.
    /// </summary>
    public static class ResearchLoopGene
    {
        public const string GeneDefName = "RL_TechCycle";

        private static bool defUnavailable;

        public static GeneDef Def
        {
            get
            {
                // NEVER cache the def object: a language switch clears and rebuilds every
                // DefDatabase (PlayDataLoader.ClearAllPlayData), so a cached reference goes stale
                // the moment the player changes language. HasGene compares by object identity, so
                // a stale def would make every pawn look gene-less and EnsureAll would attach a
                // SECOND 「科技循环」 gene carrying the old language's label — the same failure
                // that doubled the loop buff hediff (user report 2026-10-25).
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(GeneDefName);
                if (def == null && !defUnavailable)
                {
                    defUnavailable = true;
                    Log.Error("[ResearchLoops] gene def '" + GeneDefName + "' is missing " +
                              "(expected Defs/GeneDefs/ResearchLoopGene.xml); the skill bonuses " +
                              "still apply, but they are no longer shown on pawns.");
                }
                return def;
            }
        }

        /// <summary>False without Biotech: pawn.genes is null there, so the gene cannot exist.</summary>
        public static bool Available
        {
            get { return ModsConfig.BiotechActive && Def != null; }
        }

        public static bool HasGene(Pawn pawn)
        {
            if (pawn == null || pawn.genes == null) return false;
            GeneDef def = Def;
            if (def == null) return false;

            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i] != null && genes[i].def == def) return true;
            }
            return false;
        }

        /// <summary>Gives the gene to one eligible pawn. No-op when it is already there.</summary>
        public static void Ensure(Pawn pawn)
        {
            if (!Available || pawn == null || pawn.genes == null) return;

            // Collapse duplicate copies FIRST — an older build / Character Editor / a save round-trip
            // can leave the gene on a pawn twice, and the genes tab then shows two identical
            // 「科技循环」 tiles (user report 2026-10-25). Runs for any pawn that carries the gene,
            // before the eligibility gate below, so a now-ineligible pawn is cleaned too.
            Collapse(pawn);

            if (!LoopEligibility.IsSupported(pawn)) return;
            if (HasGene(pawn)) return;

            pawn.genes.AddGene(Def, false);   // false = endogene, not a xenogene
        }

        /// <summary>
        /// Removes every extra copy of this mod's gene from one pawn, keeping the first. The gene pass
        /// only ever ADDED when absent, so a second copy was never cleaned up and the genes tab listed
        /// the gene twice. Idempotent; called from <see cref="Ensure"/> on load and every sweep.
        /// </summary>
        private static void Collapse(Pawn pawn)
        {
            GeneDef def = Def;
            if (def == null || pawn == null || pawn.genes == null) return;

            // Iterate the ACTUAL endogene/xenogene lists, not the cached GenesListForReading snapshot:
            // the snapshot can lag a just-added/removed gene, and a stale snapshot is exactly how a
            // second copy survives every sweep. RemoveGene() invalidates the cache and notifies, so the
            // UI updates on the same tick.
            List<Gene> extras = null;
            bool kept = false;
            CollectExtras(pawn.genes.Endogenes, def, ref kept, ref extras);
            CollectExtras(pawn.genes.Xenogenes, def, ref kept, ref extras);
            if (extras == null) return;

            int removed = 0;
            for (int i = 0; i < extras.Count; i++)
            {
                try { pawn.genes.RemoveGene(extras[i]); removed++; }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] gene dedupe remove failed: " + ex, 0x6A01B);
                }
            }
            if (removed > 0)
            {
                Log.Warning("[ResearchLoops] removed " + removed + " duplicate '" + def.defName +
                            "' gene(s) from " + (pawn.LabelShort ?? "a pawn"));
            }
        }

        private static void CollectExtras(List<Gene> genes, GeneDef def, ref bool kept, ref List<Gene> extras)
        {
            if (genes == null) return;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene g = genes[i];
                if (g == null || g.def != def) continue;
                if (!kept) { kept = true; continue; }
                if (extras == null) extras = new List<Gene>();
                extras.Add(g);
            }
        }

        /// <summary>Gives the gene to every pawn the mod is allowed to buff.</summary>
        public static void EnsureAll()
        {
            if (!Available) return;

            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                Ensure(pawn);
            }
        }

        /// <summary>
        /// The bonus currently written into the gene def for one skill (0 without Biotech, or when
        /// the gene def is missing). Patch_SkillLevel subtracts this before adding the live bonus,
        /// so the two can never be counted twice.
        /// </summary>
        public static int AptitudeFor(SkillDef skill)
        {
            if (!ModsConfig.BiotechActive || skill == null) return 0;

            GeneDef def = Def;
            if (def == null || def.aptitudes == null) return 0;

            for (int i = 0; i < def.aptitudes.Count; i++)
            {
                Aptitude aptitude = def.aptitudes[i];
                if (aptitude != null && aptitude.skill == skill) return aptitude.level;
            }
            return 0;
        }

        /// <summary>
        /// Rewrites the gene's aptitude levels from the colony's completed loop levels. Cheap (seven
        /// entries), so it runs after every completed level rather than on a timer.
        ///
        /// The gene card (GeneUIUtility.DrawGeneDef) renders the gene's aptitude list out of
        /// GeneDef.DescriptionFull — which vanilla caches in a private field (cachedDescription) the
        /// moment it is first read. Mutating def.aptitudes underneath that cache leaves the card
        /// showing the pre-mutation numbers forever, so the cache must be dropped here or the gene
        /// card never updates (user report: 「科技循环基因不会刷新数值」). The field is reached by
        /// reflection because it is private on GeneDef.
        /// </summary>
        public static void SyncAptitudes(GameComponent_ResearchLoops component)
        {
            GeneDef def = Def;
            if (def == null || def.aptitudes == null || component == null) return;

            bool changed = false;
            for (int i = 0; i < def.aptitudes.Count; i++)
            {
                Aptitude aptitude = def.aptitudes[i];
                if (aptitude == null || aptitude.skill == null) continue;

                int bonus = component.SkillBonusFor(aptitude.skill.defName);
                if (aptitude.level != bonus)
                {
                    aptitude.level = bonus;
                    changed = true;
                }
            }

            if (changed)
            {
                InvalidateDescription(def);
                DirtyAptitudes();
            }
        }

        /// <summary>
        /// Drops GeneDef.cachedDescription so the next DescriptionFull read re-runs
        /// GetDescriptionFull and picks up the new aptitude levels. Reflection-only; the field is
        /// private and its name is pinned in the verifier so a game update that renames it fails
        /// loudly instead of silently freezing the gene card.
        /// </summary>
        private static void InvalidateDescription(GeneDef def)
        {
            if (def == null || cachedDescriptionField == null) return;
            try
            {
                cachedDescriptionField.SetValue(def, null);
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[ResearchLoops] could not invalidate gene description cache: " + ex, 0x91A2B);
            }
        }

        private static readonly FieldInfo cachedDescriptionField =
            typeof(GeneDef).GetField("cachedDescription", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Drops every pawn's cached SkillRecord.Aptitude. Required after the gene def is mutated:
        /// the cache has no idea a def changed underneath it.
        /// </summary>
        public static void DirtyAptitudes()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (pawn == null || pawn.skills == null) continue;

                List<SkillRecord> skills = pawn.skills.skills;
                for (int i = 0; i < skills.Count; i++)
                {
                    if (skills[i] != null) skills[i].DirtyAptitudes();
                }
            }
        }
    }
}
