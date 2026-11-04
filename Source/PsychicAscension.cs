using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// 心胜于物 (mind over matter) — the psychic ascension dangerous tech (user directive,
    /// 2026-10-22): once researched, the colony's psykers stop paying for their powers.
    ///   - casting no longer costs psyfocus,
    ///   - casting no longer adds psychic entropy,
    ///   - the psyfocus band no longer gates which psycast level may be cast.
    ///
    /// The ENTROPY half is NOT patched here. It rides the buff hediff instead: Hediff_ResearchLoop
    /// puts StatDefOf.PsychicEntropyGain at ×0 on every eligible pawn, and every entropy source in
    /// vanilla multiplies by that stat (Pawn_PsychicEntropyTracker.TryAddEntropy does
    /// <c>value * pawn.GetStatValue(PsychicEntropyGain)</c>), so one statFactor covers psycasts,
    /// entropy links, rituals and anything a mod adds later.
    ///
    /// Psyfocus has NO stat, so it needs patches — and they are all PAWN-scoped (LoopEligibility:
    /// free colonists and slaves only) rather than def-level on purpose. Zeroing
    /// AbilityDef.PsyfocusCost would be one line, but it would also make every ENEMY psycast free,
    /// which is a balance change nobody asked for.
    ///
    ///   CurrentPsyfocus      -> 1   the tracker reports a full battery. This is what makes
    ///                              vanilla's own affordability checks pass (Psycast.CanCast,
    ///                              Psycast.GizmoDisabled, Verb_CastPsycast.ValidateTarget,
    ///                              CompAbilityEffect_StopMentalState) without patching any of them.
    ///   FinalPsyfocusCost    -> 0   the number actually charged, so queued psycasts never stack
    ///                              past the bar and comp-overridden costs (Word of Trust / Calm)
    ///                              are free too.
    ///   OffsetPsyfocusDirectly      the charge itself is dropped: the pawn's real psyfocus never
    ///                              moves, whatever path asked for it.
    ///   TryAddEntropy               entropy gain is refused (returns true, so the cast proceeds).
    ///   WouldOverflowEntropy -> false  a pawn that is ALREADY overloaded may still cast.
    ///   MaxAbilityLevel      -> 6   the psyfocus-band gate disappears (psycast levels top out at 6).
    ///
    /// Every patch is a no-op until the tech is researched and is wrapped in try/catch: a patch
    /// that throws inside a psycast check every tick would be far worse than a missing bonus.
    /// Harmony arguments are bound by POSITION (__0) rather than by name so a game update that
    /// renames a parameter cannot make PatchAll throw at startup.
    /// </summary>
    public static class PsychicAscension
    {
        /// <summary>Highest psycast level in vanilla (psyfocus bands map to 2/4/6).</summary>
        private const int MaxVanillaPsycastLevel = 6;

        /// <summary>
        /// True when this pawn's abilities are free: the tech is researched AND the pawn is one
        /// this mod may affect (free colonist or slave — not prisoners, guests, enemies or mechs).
        /// </summary>
        public static bool ActiveFor(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!DangerousTech.Ascended) return false;
            return LoopEligibility.IsSupported(pawn);
        }

        private static void ReportOnce(string what, Exception ex, int key)
        {
            Log.ErrorOnce("[ResearchLoops] psychic ascension " + what + " patch failed: " + ex, key);
        }

        // ───────────────────────────── psyfocus ─────────────────────────────

        /// <summary>
        /// The tracker always reports a full psyfocus bar for an ascended pawn. Every vanilla
        /// affordability check compares a cost against this number, so one lie removes them all —
        /// and since nothing ever drains the real value (see Patch_OffsetPsyfocus), the two can
        /// only disagree by the slow passive fall, which is what makes meditation unnecessary.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.CurrentPsyfocus),
            MethodType.Getter)]
        public static class Patch_CurrentPsyfocus
        {
            public static void Postfix(Pawn_PsychicEntropyTracker __instance, ref float __result)
            {
                try
                {
                    if (__result >= 1f) return;
                    if (ActiveFor(__instance.Pawn)) __result = 1f;
                }
                catch (Exception ex)
                {
                    ReportOnce("CurrentPsyfocus", ex, 731001);
                }
            }
        }

        /// <summary>The amount actually charged for a cast — zero for an ascended caster.</summary>
        [HarmonyPatch(typeof(Ability), nameof(Ability.FinalPsyfocusCost))]
        public static class Patch_FinalPsyfocusCost
        {
            public static void Postfix(Ability __instance, ref float __result)
            {
                try
                {
                    if (__result <= 0f) return;
                    if (ActiveFor(__instance.pawn)) __result = 0f;
                }
                catch (Exception ex)
                {
                    ReportOnce("FinalPsyfocusCost", ex, 731002);
                }
            }
        }

        /// <summary>
        /// Drops the charge itself. Vanilla spends psyfocus through this one method, and its
        /// positive uses (drugs, weapon traits, caravan events) must keep working, so only
        /// negative offsets are swallowed.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.OffsetPsyfocusDirectly))]
        public static class Patch_OffsetPsyfocus
        {
            public static bool Prefix(Pawn_PsychicEntropyTracker __instance, float __0)
            {
                try
                {
                    if (__0 >= 0f) return true;
                    return !ActiveFor(__instance.Pawn);
                }
                catch (Exception ex)
                {
                    ReportOnce("OffsetPsyfocusDirectly", ex, 731003);
                    return true;
                }
            }
        }

        // ───────────────────────────── entropy ─────────────────────────────

        /// <summary>
        /// Refuses entropy gain for an ascended pawn. Returning true means "accepted" — the caller
        /// (Psycast.Activate) only uses the result to decide whether the cast may proceed.
        /// Negative values (entropy relief) are left alone.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.TryAddEntropy))]
        public static class Patch_TryAddEntropy
        {
            public static bool Prefix(Pawn_PsychicEntropyTracker __instance, float __0, ref bool __result)
            {
                try
                {
                    if (__0 <= 0f) return true;
                    if (!ActiveFor(__instance.Pawn)) return true;
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    ReportOnce("TryAddEntropy", ex, 731004);
                    return true;
                }
            }
        }

        /// <summary>
        /// A pawn that entered the ascension while already overloaded must not be locked out: with
        /// the entropy stat at ×0 nothing can overflow, but the CURRENT value can still sit above
        /// the cap until it dissipates.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.WouldOverflowEntropy))]
        public static class Patch_WouldOverflowEntropy
        {
            public static bool Prefix(Pawn_PsychicEntropyTracker __instance, ref bool __result)
            {
                try
                {
                    if (!ActiveFor(__instance.Pawn)) return true;
                    __result = false;
                    return false;
                }
                catch (Exception ex)
                {
                    ReportOnce("WouldOverflowEntropy", ex, 731005);
                    return true;
                }
            }
        }

        /// <summary>
        /// The psyfocus-band gate: vanilla refuses a level-N psycast unless the caster's psyfocus
        /// band is high enough (bands allow 2 / 4 / 6). Psyfocus is no longer a resource, so the
        /// whole range is always available.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.MaxAbilityLevel),
            MethodType.Getter)]
        public static class Patch_MaxAbilityLevel
        {
            public static void Postfix(Pawn_PsychicEntropyTracker __instance, ref int __result)
            {
                try
                {
                    if (__result >= MaxVanillaPsycastLevel) return;
                    if (ActiveFor(__instance.Pawn)) __result = MaxVanillaPsycastLevel;
                }
                catch (Exception ex)
                {
                    ReportOnce("MaxAbilityLevel", ex, 731006);
                }
            }
        }
    }
}
