using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// 合成进化 (synthetic evolution) — the mech-branch dangerous tech (user directive, 2026-10-22):
    /// once researched, the mechanitor's command link stops caring about distance.
    ///
    /// Vanilla's only range gate is <c>Pawn_MechanitorTracker.CanCommandTo</c>:
    ///   <code>
    ///   return (float)pawn.Position.DistanceToSquared(target.Cell) &lt; 620.01f;   // 24.9 tiles
    ///   </code>
    /// Every drafted-mech float menu, every multi-select goto and the white command-radius ring go
    /// through it (the menus via MechanitorUtility.InMechanitorCommandRange, which also keeps the
    /// same-map rule). Two patches therefore cover the whole feature:
    ///   - CanCommandTo: the distance test is lifted, but the OUT-OF-BOUNDS test is kept — a target
    ///     outside the map must stay uncommandable or pathing breaks.
    ///   - DrawCommandRadius: the 24.9 ring is no longer drawn, because a ring that no longer
    ///     limits anything is a lie on screen.
    ///   - the bandwidth budget (Notify_BandwidthChanged and friends): commanding mechs costs none.
    ///   - Need_MechEnergy: a mech never drains, so it never recharges and never shuts itself down.
    ///
    /// The MECH side of 合成进化 is not here: it is <see cref="LoopEligibility.IsLoopTarget"/>,
    /// which lets colony mechs carry the loop buff (and the three patches that make sense for a
    /// mech: ranged damage, ranged accuracy, body-part HP).
    ///
    /// The tracker's pawn is a PRIVATE field with no accessor (verified in 1.6.4871), so it is read
    /// through a Harmony FieldRef — the same O(1) trick Patch_FoodFall uses for Need.pawn, built
    /// lazily so a rename degrades to "range limit stays" plus one log line instead of throwing
    /// while PatchAll runs. Tests/Verify.cs pins the field name.
    /// </summary>
    public static class SyntheticAscension
    {
        private static AccessTools.FieldRef<Pawn_MechanitorTracker, Pawn> pawnRef;
        private static bool pawnRefUnavailable;

        /// <summary>Reads Pawn_MechanitorTracker.pawn in O(1); null when the field is unavailable.</summary>
        private static Pawn MechanitorOf(Pawn_MechanitorTracker tracker)
        {
            if (pawnRefUnavailable) return null;
            if (pawnRef == null)
            {
                try
                {
                    pawnRef = AccessTools.FieldRefAccess<Pawn_MechanitorTracker, Pawn>("pawn");
                }
                catch (Exception ex)
                {
                    pawnRefUnavailable = true;
                    Log.Error("[ResearchLoops] Pawn_MechanitorTracker.pawn accessor unavailable; the " +
                              "合成进化 command range is disabled on this game version. (" + ex.Message +
                              ") (reported once)");
                    return null;
                }
            }
            return pawnRef(tracker);
        }

        private static AccessTools.FieldRef<Pawn_MechanitorTracker, List<Pawn>> controlledPawnsRef;
        private static bool controlledPawnsRefUnavailable;

        /// <summary>Reads Pawn_MechanitorTracker.controlledPawns in O(1); null when unavailable.</summary>
        private static List<Pawn> ControlledPawnsOf(Pawn_MechanitorTracker tracker)
        {
            if (controlledPawnsRefUnavailable) return null;
            if (controlledPawnsRef == null)
            {
                try
                {
                    controlledPawnsRef = AccessTools.FieldRefAccess<Pawn_MechanitorTracker, List<Pawn>>("controlledPawns");
                }
                catch (Exception ex)
                {
                    controlledPawnsRefUnavailable = true;
                    Log.Error("[ResearchLoops] Pawn_MechanitorTracker.controlledPawns accessor unavailable; " +
                              "合成进化 infinite bandwidth is disabled on this game version. (" + ex.Message +
                              ") (reported once)");
                    return null;
                }
            }
            return controlledPawnsRef(tracker);
        }

        /// <summary>
        /// True when this mechanitor commands without a range limit: 合成进化 is researched AND the
        /// mechanitor is one of ours (a prisoner or guest mechanitor does not get it).
        /// </summary>
        public static bool ActiveFor(Pawn mechanitor)
        {
            if (mechanitor == null) return false;
            if (!DangerousTech.SyntheticAscended) return false;
            return LoopEligibility.IsSupported(mechanitor);
        }

        /// <summary>
        /// Lifts the 24.9-tile distance test. The original result is only overridden when the
        /// target is inside the map — out-of-bounds stays false, exactly as vanilla decided.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanCommandTo))]
        public static class Patch_CanCommandTo
        {
            public static void Postfix(Pawn_MechanitorTracker __instance, LocalTargetInfo __0, ref bool __result)
            {
                try
                {
                    if (__result) return;

                    Pawn mechanitor = MechanitorOf(__instance);
                    if (!ActiveFor(mechanitor)) return;
                    Map map = mechanitor.MapHeld;
                    if (map == null) return;
                    if (!__0.Cell.InBounds(map)) return;

                    __result = true;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] CanCommandTo postfix failed: " + ex, 732001);
                }
            }
        }

        /// <summary>
        /// Stops drawing the command-radius ring once it no longer limits anything. The ring is a
        /// pure readout (GenDraw.DrawRadiusRing), so skipping it changes no game state.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.DrawCommandRadius))]
        public static class Patch_DrawCommandRadius
        {
            public static bool Prefix(Pawn_MechanitorTracker __instance)
            {
                try
                {
                    return !ActiveFor(MechanitorOf(__instance));
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] DrawCommandRadius prefix failed: " + ex, 732002);
                    return true;
                }
            }
        }

        /// <summary>
        /// 合成进化 makes mechs consume no bandwidth: every overseen mech stays under control and the
        /// budget is never exhausted. Vanilla's only hard limiter is the cumulative-cost cap inside
        /// Notify_BandwidthChanged (which drops mechs past TotalBandwidth and calls
        /// Notify_DisconnectedFromOverseer on them), so this prefix skips that cap entirely and
        /// collects the full OverseenPawns set as controlled instead. CheckAvailableBandwidthForBills
        /// is still called so gestation bills are never suspended (HasBandwidthForBill is patched to
        /// always pass).
        /// </summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_BandwidthChanged))]
        public static class Patch_NotifyBandwidthChanged
        {
            public static bool Prefix(Pawn_MechanitorTracker __instance)
            {
                try
                {
                    Pawn mechanitor = MechanitorOf(__instance);
                    if (!ActiveFor(mechanitor)) return true; // not our mechanitor: run vanilla
                    List<Pawn> controlled = ControlledPawnsOf(__instance);
                    if (controlled == null) return true;     // field unavailable: degrade to vanilla
                    __instance.CheckAvailableBandwidthForBills();
                    controlled.Clear();
                    foreach (Pawn mech in __instance.OverseenPawns)
                    {
                        controlled.Add(mech);
                    }
                    return false; // skip vanilla cap logic
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] Notify_BandwidthChanged prefix failed: " + ex, 732003);
                    return true;
                }
            }
        }

        /// <summary>
        /// The bandwidth meter reads UsedBandwidth for its header and fill bar. With 合成进化 the used
        /// amount is always zero — mechs no longer draw from the pool — so the gizmo cleanly shows
        /// "0 / N" with an empty bar rather than a misleading red "over budget" state.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), "get_UsedBandwidth")]
        public static class Patch_UsedBandwidth
        {
            public static void Postfix(Pawn_MechanitorTracker __instance, ref int __result)
            {
                try
                {
                    if (!ActiveFor(MechanitorOf(__instance))) return;
                    __result = 0;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] UsedBandwidth postfix failed: " + ex, 732004);
                }
            }
        }

        /// <summary>Linking a new mech must never be blocked by the bandwidth budget.</summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanOverseeSubject))]
        public static class Patch_CanOverseeSubject
        {
            public static void Postfix(Pawn_MechanitorTracker __instance, Pawn subject, ref bool __result)
            {
                try
                {
                    if (!ActiveFor(MechanitorOf(__instance))) return;
                    if (subject != null && subject.OverseerSubject != null) __result = true;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] CanOverseeSubject postfix failed: " + ex, 732005);
                }
            }
        }

        /// <summary>Gestation bills must never be suspended for lack of bandwidth.</summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.HasBandwidthForBill))]
        public static class Patch_HasBandwidthForBill
        {
            public static void Postfix(Pawn_MechanitorTracker __instance, Bill_Mech bill, ref bool __result)
            {
                try
                {
                    if (!ActiveFor(MechanitorOf(__instance))) return;
                    __result = true;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] HasBandwidthForBill postfix failed: " + ex, 732006);
                }
            }
        }

        /// <summary>Resurrecting a mech must never be blocked by the bandwidth budget.</summary>
        [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.HasBandwidthToResurrect))]
        public static class Patch_HasBandwidthToResurrect
        {
            public static void Postfix(Pawn_MechanitorTracker __instance, Corpse corpse, ref bool __result)
            {
                try
                {
                    if (!ActiveFor(MechanitorOf(__instance))) return;
                    __result = true;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] HasBandwidthToResurrect postfix failed: " + ex, 732007);
                }
            }
        }

        private static AccessTools.FieldRef<PawnCapacitiesHandler, Pawn> capacityPawnRef;
        private static bool capacityPawnRefUnavailable;

        /// <summary>Reads PawnCapacitiesHandler.pawn in O(1); null when the field is unavailable.</summary>
        private static Pawn PawnOf(PawnCapacitiesHandler handler)
        {
            if (capacityPawnRefUnavailable) return null;
            if (capacityPawnRef == null)
            {
                try
                {
                    capacityPawnRef = AccessTools.FieldRefAccess<PawnCapacitiesHandler, Pawn>("pawn");
                }
                catch (Exception ex)
                {
                    capacityPawnRefUnavailable = true;
                    Log.Error("[ResearchLoops] PawnCapacitiesHandler.pawn accessor unavailable; 合成进化's " +
                              "organ-independent capacities are disabled on this game version. (" +
                              ex.Message + ") (reported once)");
                    return null;
                }
            }
            return capacityPawnRef(handler);
        }

        /// <summary>
        /// 合成进化's third gift (user directive 2026-10-23 「合成进化再加入一条，器官血量不影响效果，
        /// 即哪怕腿没了也不影响移动能力」): every capacity reads as full whatever the body underneath
        /// is doing.
        ///
        /// Vanilla derives a capacity from the parts that feed it — a wounded leg lowers Movement and a
        /// MISSING one zeroes it (the workers only walk GetNotMissingParts, so there is nothing left to
        /// average). 合成进化 swaps the meat for machinery, so the body's own condition must stop
        /// mattering: a level that came in below 1 is lifted to 1, which is vanilla's healthy maximum.
        ///
        /// Why the level and not the parts: patching every CapacityWorker's part walk would leave the
        /// missing-limb case exactly as broken as before, and that case is the one the user named. The
        /// read is the single choke point vanilla itself uses (PawnCapacitiesHandler.GetLevel), so one
        /// postfix covers movement, manipulation, sight, hearing, breathing, consciousness, blood
        /// pumping, digestion — every capacity, present and future.
        ///
        /// A pawn that is already at or above 1 is untouched, so 设计进化's capacity offset still
        /// stacks on top. Prisoners, guests and animals never qualify (LoopEligibility), so this is not
        /// a global rule.
        /// </summary>
        [HarmonyPatch(typeof(PawnCapacitiesHandler), "GetLevel")]
        public static class Patch_CapacityLevel
        {
            public static void Postfix(PawnCapacitiesHandler __instance, ref float __result)
            {
                try
                {
                    if (!DangerousTech.SyntheticAscended) return;
                    if (__result >= 1f) return;

                    Pawn pawn = PawnOf(__instance);
                    if (!LoopEligibility.IsLoopTarget(pawn)) return;
                    __result = 1f;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] GetLevel postfix failed: " + ex, 732008);
                }
            }
        }

        /// <summary>
        /// A mech is "controlled" iff CompOverseerSubject.State == Overseen, which vanilla decides by
        /// <c>overseer.mechanitor.ControlledPawns.Contains(mech)</c>. A mech that joined BEFORE its new
        /// overseer counted as a supported colonist (join-event timing: the pawn is not yet a free
        /// colonist when the mech is assigned) never lands in ControlledPawns, and the event-driven
        /// Notify_BandwidthChanged never re-runs — so it stayed "RequiresBandwidth" forever and showed
        /// 「带宽不足 / 未受控」 even with 合成进化 done (user report 2026-10-25). With the ascension the
        /// budget is irrelevant, so the state is forced to Overseen for an ascended overseer.
        /// </summary>
        [HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State), MethodType.Getter)]
        public static class Patch_OverseerState
        {
            public static void Postfix(CompOverseerSubject __instance, ref OverseerSubjectState __result)
            {
                try
                {
                    if (__result != OverseerSubjectState.RequiresBandwidth) return;

                    Pawn mech = __instance.Parent;
                    Pawn overseer = mech?.relations?.GetFirstDirectRelationPawn(PawnRelationDefOf.Overseer);
                    if (!ActiveFor(overseer)) return;

                    __result = OverseerSubjectState.Overseen;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] OverseerSubjectState postfix failed: " + ex, 732009);
                }
            }
        }

        private static AccessTools.FieldRef<Need, Pawn> needPawnRef;
        private static bool needPawnRefUnavailable;

        /// <summary>Reads the pawn behind a need in O(1); null when the field is unavailable.</summary>
        private static Pawn PawnOf(Need need)
        {
            if (needPawnRefUnavailable) return null;
            if (needPawnRef == null)
            {
                try
                {
                    needPawnRef = AccessTools.FieldRefAccess<Need, Pawn>("pawn");
                }
                catch (Exception ex)
                {
                    needPawnRefUnavailable = true;
                    Log.Error("[ResearchLoops] Need.pawn accessor unavailable; 合成进化's no-recharge " +
                              "rule is disabled on this game version. (" + ex.Message +
                              ") (reported once)");
                    return null;
                }
            }
            return needPawnRef(need);
        }

        /// <summary>
        /// True when this mech never needs a charger: 合成进化 is researched and the mech is ours
        /// (an enemy or feral mech keeps its battery).
        /// </summary>
        private static bool NoChargeFor(Pawn mech)
        {
            if (!DangerousTech.SyntheticAscended) return false;
            return LoopEligibility.IsColonyMech(mech);
        }

        /// <summary>
        /// 合成进化's fourth gift (user directive 2026-11-04 「机械体不需要充电」): the battery is
        /// gone as a constraint.
        ///
        /// Vanilla drains it in <c>Need_MechEnergy.NeedInterval</c> (10/day while active, 3/day
        /// idle, scaled by the 机械能耗 chain) and empties it further every time a mech is repaired
        /// (<c>MechEnergyLossPerHP</c>). At zero the mech gets the SelfShutdown hediff, starts the
        /// self-shutdown job and stops taking orders until it has charged back to 15.
        ///
        /// FallPerDay is the drain's only source, so zeroing it stops the bar moving; the shutdown
        /// bookkeeping is deliberately left to vanilla — <see cref="TopUpEnergy"/> lifts the level to
        /// full and vanilla's own NeedInterval then clears the flag, removes the hediff and overrides
        /// the job. Duplicating that here would mean re-implementing a private field's state machine.
        /// </summary>
        [HarmonyPatch(typeof(Need_MechEnergy), "get_FallPerDay")]
        public static class Patch_MechEnergyFallPerDay
        {
            public static void Postfix(Need_MechEnergy __instance, ref float __result)
            {
                try
                {
                    if (__instance == null) return;
                    if (!DangerousTech.SyntheticAscended) return; // cheap gate: called from AI ticks
                    if (!NoChargeFor(PawnOf(__instance))) return;
                    __result = 0f;
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce("[ResearchLoops] Need_MechEnergy.FallPerDay postfix failed: " + ex, 732010);
                }
            }
        }

        /// <summary>
        /// Pins a colony mech's energy to full. Called from the ascension sweep (every 250 ticks and
        /// on every research change), which is what catches a mech already at zero when the tech
        /// lands, one that joined the colony afterwards, and one drained by a charger repair.
        /// </summary>
        public static void TopUpEnergy(Pawn mech)
        {
            if (mech == null || mech.needs == null) return;
            if (!NoChargeFor(mech)) return;

            Need_MechEnergy energy = mech.needs.energy;
            if (energy == null) return;
            if (energy.CurLevel >= energy.MaxLevel) return;
            energy.CurLevel = energy.MaxLevel;
        }

        /// <summary>
        /// Re-runs the bandwidth fix for every ascended mechanitor, so a mech that joined between
        /// sweeps lands in ControlledPawns within one tick (the event-driven Notify_BandwidthChanged
        /// can miss the join-event window). Called from the game component's 250-tick sweep. Cheap:
        /// a handful of mechanitors, and Notify_BandwidthChanged is idempotent under our prefix.
        /// </summary>
        public static void SyncAll()
        {
            if (!DangerousTech.SyntheticAscended) return;

            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (p == null || p.mechanitor == null) continue;
                if (!ActiveFor(p)) continue;
                p.mechanitor.Notify_BandwidthChanged();
            }
        }
    }
}
