using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Delivers the passion loop (delivery="passion"): writes each eligible pawn's SkillRecord.passion.
    /// This is the one channel that must WRITE the pawn — the others ride a buff hediff, a gene
    /// aptitude, or a Harmony patch — because the skill-panel flames read the field directly and a
    /// read-time bonus (as the skill channel uses) cannot change what the flames show.
    ///
    /// VSE (Vanilla Skills Expanded, workshop 3400246558) compatibility — verified by decompiling
    /// VSE.dll — drives every choice here:
    ///   - VSE keeps SkillRecord.passion as the storage field but reindexes its PassionDefs
    ///     (None=0, Minor=1, Major=2, then VSE_Apathy / VSE_Natural / VSE_Critical at 3+). The old
    ///     vanilla LearnRateFactor switch that threw NotImplementedException on an unknown value is
    ///     prefixed away by VSE, so writing a VSE tier into the field is safe (no crash).
    ///   - VSE replaces PassionExtension.IncrementPassion with a learnRateFactor-sorted "+1" that
    ///     WRAPS at the top. So we always raise passion through that method (VSE hijacks it → correct
    ///     strength order; vanilla caps at Major) and guard against the wrap by stopping before VSE's
    ///     top non-bad tier, so a pawn already near the ceiling is never cycled down into VSE_Apathy.
    ///
    /// The base passion is snapshotted per pawn/skill (see GameComponent_ResearchLoops.PassionBase*)
    /// and restored on revert (level 0 or ineligible), so the effect is fully reversible and the
    /// multiplier slider just re-derives target = base + N.
    /// </summary>
    public static class ResearchLoopPassion
    {
        internal const string LoopDefName = "RL_Passion";

        // Lazily built cache of VSE's passion tiers, populated only when VSE is present.
        private static bool vseResolved;
        private static bool vseActive;
        private static Dictionary<int, float> vseLrfByOrdinal;
        private static float vseTopLrf;

        /// <summary>Applies the loop to every eligible pawn, or reverts ineligible / un-researched ones.</summary>
        public static void ApplyAll()
        {
            GameComponent_ResearchLoops comp = GameComponent_ResearchLoops.Current;
            if (comp == null) return;
            int level = comp.GetCount(LoopDefName);
            bool active = level > 0;

            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (pawn == null || pawn.skills == null || pawn.skills.skills == null) continue;
                if (!active || !LoopEligibility.IsSupported(pawn))
                {
                    Revert(comp, pawn);
                    continue;
                }
                Apply(comp, pawn, level);
            }
        }

        private static void Apply(GameComponent_ResearchLoops comp, Pawn pawn, int level)
        {
            foreach (SkillRecord skill in pawn.skills.skills)
            {
                if (skill == null) continue;

                string key = PassKey(pawn, skill);
                int baseVal;
                if (!comp.TryGetPassionBase(key, out baseVal))
                {
                    baseVal = (int)skill.passion;
                    comp.SetPassionBase(key, baseVal);
                }

                int target = baseVal;
                for (int i = 0; i < level; i++)
                {
                    if (AtCeiling(target)) break;
                    target = (int)PassionExtension.IncrementPassion((Passion)target);
                }
                skill.passion = (Passion)target;
            }
        }

        private static void Revert(GameComponent_ResearchLoops comp, Pawn pawn)
        {
            foreach (SkillRecord skill in pawn.skills.skills)
            {
                if (skill == null) continue;
                string key = PassKey(pawn, skill);
                int baseVal;
                if (comp.TryGetPassionBase(key, out baseVal))
                {
                    skill.passion = (Passion)baseVal;
                    comp.ClearPassionBase(key);
                }
            }
        }

        private static string PassKey(Pawn pawn, SkillRecord skill)
        {
            return pawn.ThingID + ":" + skill.def.defName;
        }

        /// <summary>
        /// True when raising 'ordinal' one more step would either hit the vanilla Major cap (no VSE)
        /// or wrap past VSE's top non-bad tier into VSE_Apathy. The wrap is detected by comparing the
        /// tier's learn-rate factor against the cached top: VSE_Apathy (0.25) is the only tier whose
        /// factor is below its predecessor's, so a drop in factor is the wrap, not a legitimate rise
        /// (a rise out of Apathy goes 0.25 → 0.35, an increase).
        /// </summary>
        private static bool AtCeiling(int ordinal)
        {
            EnsureVseResolved();
            if (vseActive)
            {
                float lrf;
                if (vseLrfByOrdinal.TryGetValue(ordinal, out lrf)) return lrf >= vseTopLrf;
                return false;
            }
            return ordinal >= (int)Passion.Major;
        }

        private static void EnsureVseResolved()
        {
            if (vseResolved) return;
            vseResolved = true;
            vseLrfByOrdinal = new Dictionary<int, float>();

            Assembly vse = null;
            foreach (Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "VSE") { vse = asm; break; }
            }
            if (vse == null) return;

            System.Type mgr = vse.GetType("VSE.Passions.PassionManager");
            if (mgr == null) return;
            FieldInfo field = mgr.GetField("Passions", BindingFlags.Public | BindingFlags.Static);
            if (field == null) return;
            object passions = field.GetValue(null);
            if (passions == null) return;

            vseActive = true;
            vseTopLrf = float.MinValue;
            foreach (object defObj in (System.Array)passions)
            {
                if (defObj == null) continue;
                Def def = defObj as Def;
                if (def == null) continue;
                int ordinal = def.index;
                bool isBad = ReadBool(defObj, "isBad");
                float lrf = ReadFloat(defObj, "learnRateFactor");
                vseLrfByOrdinal[ordinal] = lrf;
                if (!isBad && lrf > vseTopLrf) vseTopLrf = lrf;
            }
        }

        private static bool ReadBool(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return false;
            object v = f.GetValue(obj);
            return v is bool && (bool)v;
        }

        private static float ReadFloat(object obj, string name)
        {
            FieldInfo f = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return 1f;
            object v = f.GetValue(obj);
            return v is float ? (float)v : 1f;
        }
    }
}
