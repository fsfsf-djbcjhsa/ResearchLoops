using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// 血肉苦弱 (cybernetic ascension) — the body-branch dangerous tech (user directive, 2026-10-22):
    /// 「不流血，不疼痛，不留疤，不要娱乐，饮食，睡觉」.
    ///
    /// Five of the six effects are NOT implemented here: the ascension lists those chains in its
    /// authored `zeroes` attribute and <see cref="DangerousTech.IsZeroed"/> treats them as zeroed.
    /// So 不再流血 / 无视疼痛 / 告别娱乐 / 告别休息 / 告别饮食 keep flowing through the one
    /// existing path — the buff's statFactors, the hunger patch, the research-node text and the
    /// buff tooltip all read the same ×0 — and this file only owns the two things that path cannot
    /// express. (2026-10-23: the five per-loop dangerous tech nodes those names came from were
    /// deleted on user request, which makes this `zeroes` list the sole implementation of them.
    /// What is left for this file to own:)
    ///
    ///  1. NO PERMANENT INJURIES. Zeroing a rate cannot stop a scar from forming; that is decided
    ///     when the injury is finalised. Vanilla's own "scarless" gene short-circuits the very same
    ///     method (HediffComp_GetsPermanent.PreFinalizeInjury checks Gene.preventPermanentWounds), so
    ///     this patch uses the official seam instead of inventing one.
    ///
    ///  2. THE NEEDS ACTUALLY GO AWAY. Zeroing JoyFallRateFactor/RestFallRateFactor/FoodFallPerTick
    ///     only FREEZES a need where it stands — a colonist who was at 20% joy when the tech landed
    ///     would carry 「非常无聊」 forever, which reads as a bug. <see cref="TopUpNeeds"/> pins the
    ///     three needs to full on the existing 250-tick buff sweep; because their fall rate is 0 they
    ///     then stay full, so no sleep, no meals and no recreation are ever needed again.
    ///
    ///  3. ARMOUR PINNED AT 200%. A pinned value cannot be authored (see
    ///     <see cref="Patch_ArmorPinned"/>), so blunt / sharp / heat armour of an ascended pawn is
    ///     forced to vanilla's ceiling where every read funnels through.
    /// </summary>
    public static class CyberneticAscension
    {
        /// <summary>
        /// True when this pawn's body is machinery: 血肉苦弱 is researched and the pawn is one this
        /// mod may affect (a mech has no needs or scars, so the rule stays colonist/slave).
        /// </summary>
        public static bool ActiveFor(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!DangerousTech.CyberAscended) return false;
            return LoopEligibility.IsSupported(pawn);
        }

        /// <summary>
        /// Pins food / rest / joy to full for an ascended pawn. Called from the buff sweep (every
        /// 250 ticks and on every research change), which is also what catches pawns who joined the
        /// colony after the tech was researched. Cheap: three null checks and a float compare.
        /// </summary>
        public static void TopUpNeeds(Pawn pawn)
        {
            if (pawn == null || pawn.needs == null) return;
            if (!DangerousTech.CyberAscended) return;
            if (!LoopEligibility.IsSupported(pawn)) return;

            Fill(pawn.needs.food);
            Fill(pawn.needs.rest);
            Fill(pawn.needs.joy);
        }

        /// <summary>Sets one need to full (CurLevel is normalised 0..1 in vanilla).</summary>
        private static void Fill(Need need)
        {
            if (need == null) return;
            if (need.CurLevel >= need.MaxLevel) return;
            need.CurLevel = need.MaxLevel;
        }

        /// <summary>
        /// No new permanent injuries (scars). Returning false skips vanilla's whole
        /// PreFinalizeInjury pass — the same early exit its own scarless-gene check takes — so the
        /// injury simply heals away instead of becoming a scar.
        /// </summary>
        [HarmonyPatch(typeof(HediffComp_GetsPermanent), nameof(HediffComp_GetsPermanent.PreFinalizeInjury))]
        public static class Patch_PreFinalizeInjury
        {
            public static bool Prefix(HediffComp_GetsPermanent __instance)
            {
                try
                {
                    return !ActiveFor(__instance != null ? __instance.Pawn : null);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] PreFinalizeInjury prefix failed: " + ex, 733001);
                    return true;
                }
            }
        }

        /// <summary>
        /// 血肉苦弱's seventh gift (user directive 2026-11-04 「护甲固定为200%，钝器/锐器/热能防御
        /// 均为200%」): the pinned value, which is vanilla's own armour ceiling
        /// (<c>ArmorUtility.MaxArmorRating</c> = 2). The gear tab divides the pawn stat by 2 and
        /// clamps to 0..1 before painting, so 2 renders as exactly 200% — the number the player is
        /// promised and the number the damage math uses are the same read.
        /// </summary>
        public const float PinnedArmor = 2f;

        private static AccessTools.FieldRef<StatWorker, StatDef> statWorkerStatRef;
        private static bool statWorkerStatRefUnavailable;

        /// <summary>
        /// Reads the StatDef a worker computes in O(1); null when the field is unavailable.
        ///
        /// The patch below sits on the one method every pawn stat read funnels through
        /// (<see cref="StatWorker.GetValue(StatRequest, bool)"/>, which both
        /// <c>GetValue(Thing, …)</c> overloads call), because that funnel is the only seam where
        /// the value can be pinned to an exact number — <c>StatModifier</c> has no Set operation
        /// and a hediff stage only offers statOffsets (additive) and statFactors (multiplicative),
        /// so with the pawn's base armour at 0 an additive line can never reach 2 and a factor of 0
        /// times 0 is still 0. The worker's own <c>stat</c> field is the only thing that says which
        /// stat is being computed there: <c>StatRequest</c> carries the subject, not the stat.
        ///
        /// It is a <c>protected</c> field with no accessor, and the seam is built lazily for the
        /// same reason as Need.pawn — a game update that renames it degrades to "armour stays
        /// vanilla + one log line" instead of throwing while PatchAll runs.
        /// </summary>
        private static StatDef StatOf(StatWorker worker)
        {
            if (statWorkerStatRefUnavailable) return null;
            if (statWorkerStatRef == null)
            {
                try
                {
                    statWorkerStatRef = AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");
                }
                catch (Exception ex)
                {
                    statWorkerStatRefUnavailable = true;
                    Log.Error("[ResearchLoops] StatWorker.stat accessor unavailable; 血肉苦弱's pinned " +
                              "armour is disabled on this game version. (" + ex.Message +
                              ") (reported once)");
                    return null;
                }
            }
            return statWorkerStatRef(worker);
        }

        /// <summary>
        /// Blunt / sharp / heat armour of an ascended colonist or slave is 2.0, whatever the body,
        /// the apparel or any other chain says. <see cref="ActiveFor"/> is the gate, so prisoners,
        /// guests, caravans, mechs and animals keep their real armour.
        ///
        /// Two consumers, one seam: <c>ArmorUtility.GetPostArmorDamage</c> reads
        /// <c>pawn.GetStatValue(armorRatingStat)</c> after the per-garment pass, and
        /// <c>ITab_Pawn_Gear.TryDrawOverallArmor</c> reads the same stat for the bars — so the
        /// number used to deflect a bullet and the number drawn in the health/gear tab cannot drift
        /// apart, which is what would happen if only the damage call were patched.
        ///
        /// Stat identity is checked first and is three reference compares against freshly-read
        /// <c>StatDefOf</c> fields (never cached here: a language switch rebuilds the def objects,
        /// and a cached one would silently stop matching). Armour stats are only read on injury and
        /// on GUI paint, so the two dictionary lookups behind <c>CyberAscended</c> sit behind that
        /// filter rather than in a per-tick path.
        /// </summary>
        [HarmonyPatch(typeof(StatWorker), "GetValue", new[] { typeof(StatRequest), typeof(bool) })]
        public static class Patch_ArmorPinned
        {
            public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
            {
                try
                {
                    StatDef stat = StatOf(__instance);
                    if (stat == null) return;
                    if (stat != StatDefOf.ArmorRating_Sharp &&
                        stat != StatDefOf.ArmorRating_Blunt &&
                        stat != StatDefOf.ArmorRating_Heat) return;
                    if (!DangerousTech.CyberAscended) return;

                    // req.Thing is the subject of the read; req.Pawn is the "compute this for whom"
                    // context, which is a different pawn for e.g. a worn garment's stat line.
                    if (!LoopEligibility.IsSupported(req.Thing as Pawn)) return;

                    __result = PinnedArmor;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] StatWorker.GetValue armour postfix failed: " + ex, 733002);
                }
            }
        }
    }
}
