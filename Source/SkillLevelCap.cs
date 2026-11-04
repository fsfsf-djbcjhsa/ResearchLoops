using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The built-in "skills have no level 20 cap" feature.
    ///
    /// Reference mod: EndlessGrowth (SlimeSenpai, workshop 2894401734) — "This mod removes the skill
    /// cap of 20". That effect is needed here anyway: the work loops run to 50-100 levels and every
    /// one of them is a skill point, so asking players to install a second mod for the exact same
    /// change made no sense.
    ///
    /// Vanilla caps a skill in four places, all neutralised below:
    ///
    ///   1. SkillRecord.Level (setter)   levelInt = Clamp(value, 0, 20)
    ///   2. SkillRecord.GetLevel         Clamp(levelInt + Aptitude, 0, 20)
    ///   3. SkillRecord.GetLevelForUI    the same clamp, its own body
    ///   4. SkillRecord.Learn            levels stop at 20 and XP is capped at that level's cost,
    ///                                    so nothing past 19 can ever be earned
    ///
    /// 2 and 3 are the ones that actually block this mod: they also clamp the gene's aptitude, which
    /// is what carries the loop bonus. 4 is what a player would otherwise still hit in normal play.
    ///
    /// Every transpiler here is a REPLACEMENT, never a rewrite: when the instruction pattern is
    /// missing (another mod already patched it, or a game update changed the method body) the patch
    /// returns the original instructions untouched instead of corrupting IL.
    /// </summary>
    public static class SkillLevelCap
    {
        /// <summary>
        /// XP per level past 19. Vanilla's curve stops at level 19, so without this, XP income would
        /// simply be discarded once a pawn reached the point where the curve runs out. The vanilla
        /// points are kept exactly; the tail (29..99) is the same one EndlessGrowth uses, i.e. the
        /// slope vanilla already had at level 19.
        /// </summary>
        private static readonly SimpleCurve XpForUncappedLevelUpCurve = new SimpleCurve
        {
            new CurvePoint(0f, 1000f),
            new CurvePoint(9f, 10000f),
            new CurvePoint(19f, 30000f),
            new CurvePoint(29f, 60000f),
            new CurvePoint(39f, 100000f),
            new CurvePoint(49f, 150000f),
            new CurvePoint(59f, 220000f),
            new CurvePoint(69f, 300000f),
            new CurvePoint(79f, 400000f),
            new CurvePoint(89f, 520000f),
            new CurvePoint(99f, 650000f)
        };

        /// <summary>
        /// Ceiling for levels EARNED BY EXPERIENCE. The loop bonus is not capped (GetLevel has no
        /// upper clamp any more); only learning through play stops here, which is high enough that
        /// the skill panel never shows a number a player did not expect.
        /// </summary>
        private const int LearnCap = 100;

        // ─────────────── 1/2/3: Clamp(v, 0, 20) → Max(v, 0) ───────────────

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Level), MethodType.Setter)]
        public static class Patch_SkillRecord_LevelSetter
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return DropUpperClamp(instructions);
            }
        }

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
        public static class Patch_SkillRecord_GetLevel
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return DropUpperClamp(instructions);
            }
        }

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
        public static class Patch_SkillRecord_GetLevelForUI
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return DropUpperClamp(instructions);
            }
        }

        /// <summary>
        /// Replaces Mathf.Clamp(value, 0, 20) with Mathf.Max(value, 0): the lower bound stays, the
        /// 20 goes. Guarded by a pattern match, so it is a no-op when there is no clamp to remove.
        /// </summary>
        private static IEnumerable<CodeInstruction> DropUpperClamp(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions, null);
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)20),
                new CodeMatch(OpCodes.Call,
                    AccessTools.Method(typeof(Mathf), "Clamp", new[] { typeof(int), typeof(int), typeof(int) })));
            if (matcher.IsInvalid)
            {
                return instructions;   // no clamp here (already patched, or a game change)
            }

            matcher.RemoveInstruction();   // drop the "20" so the call keeps two arguments
            matcher.SetInstruction(new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(Mathf), "Max", new[] { typeof(int), typeof(int) })));
            return matcher.InstructionEnumeration();
        }

        // ─────────────── 4: XP past 19, and Learn's own stop ───────────────

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
        public static class Patch_SkillRecord_Learn
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // Vanilla Learn carries the cap three times: the "levelInt == 20" overflow guard,
                // and "levelInt >= 20" plus "levelInt = 20" inside the level-up loop. Swapping every
                // literal 20 for LearnCap keeps the shape of the method exactly — the overflow guard
                // stops firing, and the loop's own cap moves from 20 to 100.
                CodeMatcher matcher = new CodeMatcher(instructions, null);
                bool changed = false;

                while (matcher.IsValid)
                {
                    if (matcher.Opcode == OpCodes.Ldc_I4_S && matcher.Operand is sbyte literal && literal == 20)
                    {
                        matcher.SetInstruction(new CodeInstruction(OpCodes.Ldc_I4, LearnCap));
                        changed = true;
                    }
                    matcher.Advance();
                }

                if (!changed)
                {
                    Log.Message("[ResearchLoops] Learn(): no level-20 cap found — already uncapped.");
                }
                return matcher.InstructionEnumeration();
            }
        }

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.XpRequiredToLevelUpFrom))]
        public static class Patch_SkillRecord_XpRequiredToLevelUpFrom
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                CodeMatcher matcher = new CodeMatcher(instructions, null);
                matcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(SkillRecord), "XpForLevelUpCurve")));
                if (matcher.IsInvalid)
                {
                    Log.Warning("[ResearchLoops] XpRequiredToLevelUpFrom(): XP curve field not found — " +
                                "XP past level 19 will not scale.");
                    return instructions;
                }

                matcher.SetInstruction(new CodeInstruction(OpCodes.Ldsfld,
                    AccessTools.Field(typeof(SkillLevelCap), "XpForUncappedLevelUpCurve")));
                return matcher.InstructionEnumeration();
            }
        }

        // ─────────────── the level word: vanilla only translates up to Skill20 ───────────────

        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.LevelDescriptor), MethodType.Getter)]
        public static class Patch_SkillRecord_LevelDescriptor
        {
            public static void Postfix(ref string __result, SkillRecord __instance)
            {
                int level = __instance == null ? 0 : __instance.GetLevelForUI(true);
                if (level <= 20) return;

                // Vanilla's switch falls through to "Unknown" above 20 and has no translations past
                // that, so: use a translation for this level when one exists (another mod may have
                // added some), otherwise reuse the highest word vanilla ships.
                string wanted = "Skill" + level;
                string translated = wanted.Translate().ToString();
                string fallback = "Skill20".Translate().ToString();
                __result = translated == wanted ? fallback : translated;
            }
        }

        // ─────────────── 5: crafting quality past level 20 ───────────────

        /// <summary>
        /// Vanilla's GenerateQualityCreatedByPawn(int) is a switch(level) with cases 0..20 ONLY. Above
        /// 20 no case matches, so `num` stays 0 and the item comes out WORSE than a level-0 craft — the
        /// "workbench stops working" a 50-level skill chain produces (user report 2026-10-25). Levels
        /// 0..20 keep vanilla exactly; above 20 we extrapolate the 19->20 slope (+0.1/level, i.e.
        /// 4.2 + 0.1*(level-20)) and replay the same Gaussian + clamp + inspiration the vanilla body
        /// does, so a high-skill crafter keeps improving instead of collapsing to junk.
        ///
        /// The (Pawn, SkillDef, bool) overload routes through this int overload, so both are covered.
        /// </summary>
        [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
            new[] { typeof(int), typeof(bool) })]
        public static class Patch_QualityPastTwenty
        {
            public static bool Prefix(int relevantSkillLevel, bool inspired, ref QualityCategory __result)
            {
                if (relevantSkillLevel <= 20) return true;   // vanilla path, byte-for-byte unchanged

                float num = 4.2f + 0.1f * (relevantSkillLevel - 20);
                int value = (int)Rand.GaussianAsymmetric(num, 0.6f, 0.8f);
                value = Mathf.Clamp(value, 0, 5);
                if (value == 5 && Rand.Value < 0.5f)
                {
                    value = (int)Rand.GaussianAsymmetric(num, 0.6f, 0.95f);
                    value = Mathf.Clamp(value, 0, 5);
                }
                QualityCategory quality = (QualityCategory)value;
                if (inspired) quality = (QualityCategory)Mathf.Min((int)quality + 2, 6); // == QualityUtility.AddLevels
                __result = quality;
                return false;
            }
        }

        // ─────────────── 6: bill "worker skill range" ceiling 20 -> 100 ───────────────

        // User report 2026-10-25: a skill-25 pawn was rejected by a bill ("AboveAllowedSkill")
        // because Bill.allowedSkillRange defaults to (0, 20) and the check is num > max. Now that
        // skills go past 20, the default ceiling excludes exactly the pawns the mod empowers. The
        // three patches below lift the ceiling to 100: new bills default to (0,100), old saved bills
        // are widened 20 -> 100 on load, and the config slider can actually reach 100.

        private const int BillSkillCeiling = 100;

        /// <summary>New bills: default (0,20) -> (0,100). Loaded bills are restored by ExposeData after this.</summary>
        [HarmonyPatch]
        public static class Patch_BillDefaultSkillRange
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Constructor(typeof(Bill));
                yield return AccessTools.Constructor(typeof(Bill),
                    new[] { typeof(RecipeDef), typeof(Precept_ThingStyle) });
            }

            private static void Postfix(Bill __instance)
            {
                if (__instance == null) return;
                IntRange r = __instance.allowedSkillRange;
                if (r.max < BillSkillCeiling) __instance.allowedSkillRange = new IntRange(r.min, BillSkillCeiling);
            }
        }

        /// <summary>Old saves: a bill saved with the vanilla 20 ceiling is widened to 100 on load.</summary>
        [HarmonyPatch(typeof(Bill), nameof(Bill.ExposeData))]
        public static class Patch_BillWidenSkillRangeOnLoad
        {
            private static void Postfix(Bill __instance)
            {
                if (__instance == null || Scribe.mode != LoadSaveMode.PostLoadInit) return;
                IntRange r = __instance.allowedSkillRange;
                if (r.max == 20) __instance.allowedSkillRange = new IntRange(r.min, BillSkillCeiling);
            }
        }

        /// <summary>The config dialog hardcodes the slider ceiling at 20; lift it to 100.</summary>
        [HarmonyPatch(typeof(Dialog_BillConfig), nameof(Dialog_BillConfig.DoWindowContents))]
        public static class Patch_BillSkillSliderMax
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var matcher = new CodeMatcher(instructions, null);
                MethodInfo intRange = AccessTools.Method(typeof(Listing_Standard), "IntRange",
                    new[] { typeof(IntRange).MakeByRefType(), typeof(int), typeof(int) });
                if (intRange == null) return instructions;

                matcher.MatchStartForward(new CodeMatch(ci =>
                    (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) &&
                    ci.operand is MethodInfo mi && mi == intRange));
                if (matcher.IsInvalid)
                {
                    Log.Warning("[ResearchLoops] bill skill slider: IntRange call not found; slider stays 0-20");
                    return instructions;
                }

                CodeInstruction maxArg = matcher.InstructionAt(-1);
                if (maxArg == null) return instructions;

                bool isTwenty =
                    (maxArg.opcode == OpCodes.Ldc_I4_S && maxArg.operand is sbyte sb && sb == 20) ||
                    (maxArg.opcode == OpCodes.Ldc_I4 && maxArg.operand is int iv && iv == 20);
                if (!isTwenty)
                {
                    Log.Warning("[ResearchLoops] bill skill slider: ceiling arg was not 20 (" +
                                maxArg.opcode + " " + maxArg.operand + "); slider left unchanged");
                    return instructions;
                }

                matcher.Advance(-1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Ldc_I4, BillSkillCeiling));
                return matcher.InstructionEnumeration();
            }
        }
    }
}
