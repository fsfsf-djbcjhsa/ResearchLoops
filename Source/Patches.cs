using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Harmony patches for the research-loop mechanism plus the three gameplay effects that have
    /// no StatDef to hang a StatPart on.
    ///
    /// Research flow (v4):
    ///   1. <see cref="Patch_FinishProject"/> counts one completed loop level and re-projects the
    ///      def onto the next level (cost/label/description). The def itself stays on the panel,
    ///      which is what makes "research this, get the next level" work.
    ///   2. <see cref="Patch_ProgressReal"/> offsets the raw progress by the completed levels'
    ///      cost, so every level has its own progress bar and its own cost while
    ///      ResearchManager's private progress dictionary stays untouched.
    ///
    /// There is deliberately no panel-filter patch any more: the loops live in their own
    /// ResearchTabDef page, so nothing has to be hidden from the main tree.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patches
    {
        static Patches()
        {
            ValidateGeneratedDefs();
        }

        /// <summary>
        /// Post-load self check. The def generator cannot be unit-tested from outside the game, so
        /// it reports its own consistency here: a broken def shows up as a log line rather than as
        /// a silently empty research page.
        /// </summary>
        private static void ValidateGeneratedDefs()
        {
            List<ResearchLoopTemplate> templates = ResearchLoopTemplates.All;
            if (templates.Count == 0)
            {
                Log.Error("[ResearchLoops] self-check: no loop templates were loaded.");
                return;
            }

            if (DefDatabase<ResearchTabDef>.GetNamedSilentFail(ResearchLoopsIds.TabDefName) == null)
            {
                Log.Error("[ResearchLoops] self-check: research tab '" + ResearchLoopsIds.TabDefName +
                          "' is missing (expected Defs/ResearchTabDefs/ResearchLoopsTab.xml); the loops " +
                          "would fall back into the main research tree.");
            }

            int problems = 0;
            foreach (ResearchLoopTemplate template in templates)
            {
                ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(template.defName);
                if (def == null)
                {
                    Log.Error("[ResearchLoops] self-check: missing generated def " + template.defName);
                    problems++;
                    continue;
                }

                ResearchLoopExtension ext = def.GetModExtension<ResearchLoopExtension>();
                if (ext == null || ext.loop != template.defName)
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName +
                              " has no ResearchLoopExtension for its own loop id");
                    problems++;
                }

                if (def.tab == null || def.tab.defName != ResearchLoopsIds.TabDefName)
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName + " is not in tab " +
                              ResearchLoopsIds.TabDefName);
                    problems++;
                }

                if (def.techLevel == TechLevel.Undefined)
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName + " has no techLevel");
                    problems++;
                }

                if (def.prerequisites == null || def.prerequisites.Count != 1 ||
                    def.prerequisites[0] == null || def.prerequisites[0].defName != template.prerequisite)
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName + " does not have exactly the " +
                              "authored prerequisite (" + template.prerequisite + ")");
                    problems++;
                }

                float expectedCost = ResearchLoopPlan.CostForLevel(template, 0);
                if (Math.Abs(def.baseCost - expectedCost) > 0.01f)
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName + " level-1 cost is " +
                              def.baseCost + ", expected " + expectedCost);
                    problems++;
                }

                if (def.label == null || !def.label.Contains("Lv.1/"))
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName + " label is not in level-1 " +
                              "state: " + (def.label ?? "<null>"));
                    problems++;
                }

                if (string.IsNullOrEmpty(def.description) || def.description.Contains("{"))
                {
                    Log.Error("[ResearchLoops] self-check: " + def.defName +
                              " has an unfilled description: " + (def.description ?? "<null>"));
                    problems++;
                }
            }

            if (problems == 0)
            {
                Log.Message("[ResearchLoops] self-check passed: " + templates.Count +
                            " loop defs in tab " + ResearchLoopsIds.TabDefName);
            }
            else
            {
                Log.Error("[ResearchLoops] self-check found " + problems + " problem(s) across " +
                          templates.Count + " loop defs.");
            }

            ValidateDangerousDefs(ref problems);
        }

        /// <summary>
        /// The dangerous techs are generated next to the loops but are NOT loops themselves, so
        /// they get their own pass: right def, right tab, gated on the parent chain, priced as
        /// authored. This runs in the static constructor, i.e. before any game exists — nothing
        /// here may touch game state (IsFinished would walk Find.ResearchManager, which is null
        /// at the main menu), so the cost is compared against the generation-time expression.
        /// </summary>
        private static void ValidateDangerousDefs(ref int problems)
        {
            int dangerous = 0;
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.dangerous == null) continue;
                dangerous++;

                ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(
                    DangerousTech.DefNameFor(template));
                if (def == null)
                {
                    Log.Error("[ResearchLoops] self-check: missing generated dangerous tech " +
                              DangerousTech.DefNameFor(template) + " for loop " + template.defName);
                    problems++;
                    continue;
                }

                if (def.tab == null || def.tab.defName != ResearchLoopsIds.TabDefName)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " is not in tab " + ResearchLoopsIds.TabDefName);
                    problems++;
                }

                if (def.prerequisites == null || def.prerequisites.Count != 1 ||
                    def.prerequisites[0] == null || def.prerequisites[0].defName != template.defName)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " is not gated on its parent loop " + template.defName);
                    problems++;
                }

                if (string.IsNullOrEmpty(def.label) || string.IsNullOrEmpty(def.description))
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " has no label or description");
                    problems++;
                }

                if (def.techLevel == TechLevel.Undefined)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " has no techLevel");
                    problems++;
                }

                // Same expression the generator wrote (authored cost x the live cost multiplier at
                // generation time); a completed project's frozen cost is not re-checked here.
                float scale = LoopMath.Safe(ResearchLoopsConfig.CostScale, 1f);
                if (scale <= 0f) scale = 1f;
                float expected = template.dangerous.cost * scale;
                if (Math.Abs(def.baseCost - expected) > 0.01f)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " costs " + def.baseCost + ", expected " + expected);
                    problems++;
                }
            }

            // Standalone dangerous techs (心胜于物): same checks, except that they are gated on every
            // chain of an authored list reaching `prereqLevel` (this mod's own counters) and carry a
            // coordinate of their own instead of a handover.
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                if (info == null) continue;
                dangerous++;

                ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(info.defName);
                if (def == null)
                {
                    Log.Error("[ResearchLoops] self-check: missing generated dangerous tech " + info.defName);
                    problems++;
                    continue;
                }

                if (def.tab == null || def.tab.defName != ResearchLoopsIds.TabDefName)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " is not in tab " + ResearchLoopsIds.TabDefName);
                    problems++;
                }

                // The gate is this mod's own level check (Patch_CanStartNow and
                // Patch_PrerequisitesCompleted read the chain counters), NOT vanilla's prerequisite
                // test. A vanilla prerequisite on a loop def means "that chain is 练满" — 50 levels —
                // which would quietly replace the authored prereqLevel, and it also puts vanilla's own
                // "Prerequisites:" block on the panel, coloured by IsFinished, i.e. red at the very
                // level the node opens (user, 2026-11-04: 「前置条件是 10 级不是 50 级」+「不要动 UI」).
                // So the def carries none, and the authored chain list feeds only the node's text.
                if (def.prerequisites != null && def.prerequisites.Count > 0)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " carries " + def.prerequisites.Count +
                              " vanilla prerequisites; the ascension gate is this mod's own level check " +
                              "(every chain at " + info.prereqLevel + "), and vanilla reads a " +
                              "prerequisite as that chain being finished at 满级 (authored chains: " +
                              string.Join(", ", info.prerequisites.ToArray()) + ")");
                    problems++;
                }

                if (info.prerequisites.Count == 0 || info.prereqLevel <= 0)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " has no usable gate (prerequisites " + info.prerequisites.Count +
                              ", prereqLevel " + info.prereqLevel + ")");
                    problems++;
                }

                for (int i = 0; i < info.prerequisites.Count; i++)
                {
                    if (DefDatabase<ResearchProjectDef>.GetNamedSilentFail(info.prerequisites[i]) != null)
                        continue;

                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " names a prerequisite that does not exist (" + info.prerequisites[i] +
                              "); its node would stay locked forever");
                    problems++;
                }

                if (string.IsNullOrEmpty(def.label) || string.IsNullOrEmpty(def.description))
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " has no label or description");
                    problems++;
                }

                if (def.techLevel == TechLevel.Undefined)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " has no techLevel");
                    problems++;
                }

                float scale = LoopMath.Safe(ResearchLoopsConfig.CostScale, 1f);
                if (scale <= 0f) scale = 1f;
                float expected = info.cost * scale;
                if (Math.Abs(def.baseCost - expected) > 0.01f)
                {
                    Log.Error("[ResearchLoops] self-check: dangerous tech " + def.defName +
                              " costs " + def.baseCost + ", expected " + expected);
                    problems++;
                }
            }

            if (dangerous == 0) return;
            Log.Message("[ResearchLoops] self-check: " + dangerous + " dangerous tech(s) generated " +
                        "in tab " + ResearchLoopsIds.TabDefName);
        }

        // ─────────────── research flow: count the completion, offer the next level ───────────────

        /// <summary>
        /// Counts one completion of a loop level and moves the def on to the next level.
        ///
        /// It never writes <c>progress</c> and never changes parameters beyond this mod's own
        /// defs, so non-loop research is untouched. Vanilla has already stored the finished
        /// level's cost as raw progress at this point (FinishProject does that before the
        /// postfix runs), which is exactly the amount Patch_ProgressReal subtracts.
        ///
        /// A dangerous tech is not a loop: finishing one records no counter, but it zeroes the
        /// parent chain's coefficient everywhere, so the defs and buffs are re-projected right
        /// now — otherwise the parent node would keep quoting the pre-zero number until the next
        /// settings change or load.
        /// </summary>
        [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
        public static class Patch_FinishProject
        {
            public static void Postfix(ResearchProjectDef proj)
            {
                try
                {
                    if (proj == null) return;

                    if (DangerousTech.IsDangerousDef(proj))
                    {
                        GameComponent_ResearchLoops dangerousComponent = GameComponent_ResearchLoops.Current;
                        if (dangerousComponent != null) dangerousComponent.NotifySettingsChanged();
                        // Finishing one of the four ascensions plays its sting; nested zeroing
                        // techs also land here but are not in the ascension map, so stay silent.
                        ResearchLoopsSounds.PlayAscensionIfAny(proj);
                        // 同一张表：四条飞升完成时再弹一封特殊文本信件（正文为群星官方完成
                        // 文本，中英切换）。嵌套归零科技不在表里，静默跳过。
                        AscensionPopup.Show(proj);
                        return;
                    }

                    ResearchLoopTemplate template = ResearchLoopProgress.TemplateOf(proj);
                    if (template == null) return;

                    GameComponent_ResearchLoops component = GameComponent_ResearchLoops.Current;
                    if (component == null)
                    {
                        Log.Warning("[ResearchLoops] completed " + proj.defName +
                                    " but no game component is available to record the level");
                        return;
                    }

                    component.Increment(template.defName);
                    int completed = component.GetCount(template.defName);
                    ResearchLoopProgress.Apply(proj, completed);
                    // Finishing the last level of a chain is a bigger moment than a single
                    // level-up, so it gets the grander sting. Covers both manual research and
                    // AutoLoop, since every level completion flows through here.
                    if (ResearchLoopPlan.ChainComplete(template, completed))
                        ResearchLoopsSounds.PlayChainComplete();
                    else
                        ResearchLoopsSounds.PlayResearchComplete();
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] FinishProject postfix failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Gives every loop level its own progress bar.
        ///
        /// Raw progress is never reset here (ResearchManager.progress is private and shared with
        /// vanilla bookkeeping; FinishProject writes the finished level's cost into it), so the
        /// cost of the completed levels is subtracted on read instead. Verified read-only surface:
        /// ProgressReal is used by GetTip, ProgressPercent/ProgressApparent, the panel drawing and
        /// IsFinished — no write path derives progress from it.
        /// </summary>
        [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.ProgressReal), MethodType.Getter)]
        public static class Patch_ProgressReal
        {
            public static void Postfix(ResearchProjectDef __instance, ref float __result)
            {
                try
                {
                    ResearchLoopTemplate template = ResearchLoopProgress.TemplateOf(__instance);
                    if (template == null) return;

                    int completed = GameComponent_ResearchLoops.GetLevel(template.defName);
                    __result = ResearchLoopPlan.DisplayProgress(template, completed, __result,
                        ResearchLoopsConfig.CostScale, ResearchLoopsConfig.BonusScale);
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] ProgressReal postfix failed: " + ex);
                }
            }
        }

        // ─────────────── ranged damage: the one weapon-level stat (DESIGN §5 patch 3) ───────────────

        /// <summary>
        /// Vanilla has no pawn stat for ranged damage — the number lives on the projectile def — so
        /// this scales the virtual property every projectile reads at impact, keyed on the launcher.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.DamageAmount), MethodType.Getter)]
        [HarmonyAfter("JellyCreative.IsekaiLeveling")]
        public static class Patch_ProjectileDamage
        {
            public static void Postfix(Projectile __instance, ref int __result)
            {
                try
                {
                    if (__instance == null) return;
                    Pawn pawn = __instance.Launcher as Pawn;
                    // IsLoopTarget: colony mechs join in once 合成进化 is researched.
                    // IsPlayerTurret: turrets also receive the damage bonus (发射者是建筑不是小人).
                    if (!LoopEligibility.IsLoopTarget(pawn) &&
                        !LoopEligibility.IsPlayerTurret(__instance.Launcher)) return;

                    int count = GameComponent_ResearchLoops.GetLevel("RL_RangedDamage");
                    if (count <= 0) return;

                    float mult = LoopFactor("RL_RangedDamage");
                    if (mult <= 1f) return;

                    // Precedent: truncation would swallow a small multiplier on low-damage weapons,
                    // so round up and guarantee at least +1.
                    __result = Mathf.Max(__result + 1, Mathf.CeilToInt(__result * mult));
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] Projectile.DamageAmount postfix failed: " + ex);
                }
            }
        }

        // ─────────────── ranged accuracy: the shooter's own miss chance (DESIGN §5 patch 4) ───────────────

        /// <summary>
        /// Ranged accuracy has no additive hit-chance stat to ride. ShootingAccuracyPawn is a
        /// per-cell decay BASE that vanilla raises to the power of the distance
        /// (ShotReport.HitFactorFromShooter: pow(stat, distance)), and the def's postProcessCurve
        /// re-maps the raw value before that — so a probability-sized offset on it is swallowed
        /// (that is exactly why the old melee hit / dodge chains did nothing).
        ///
        /// The one honest place for a "3% more accurate" claim is the shooter factor itself, and
        /// there it must be multiplicative on the MISS side:
        ///
        ///     f' = 1 - (1 - f) * k
        ///
        /// Adding to the hit side instead would be clipped by TotalEstimatedHitChance's Clamp01 as
        /// soon as the sum passed 100%, which is why the vanilla band factors (ShootingAccuracyFactor_*)
        /// are a dead end past ~30 levels. This form is asymptotic: no level is ever wasted, it can
        /// never exceed 1, and cover / weather / weapon accuracy are separate factors that stay
        /// untouched.
        ///
        /// Note: StatWorker_ShootingAccuracy renders a 1..55 cell table by calling the same method,
        /// so that table also reflects the buff — intended, it is the real per-distance hit chance.
        /// </summary>
        [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitFactorFromShooter))]
        public static class Patch_ShotHitFactor
        {
            public static void Postfix(Thing caster, ref float __result)
            {
                try
                {
                    if (!LoopEligibility.IsLoopTarget(caster as Pawn) &&
                        !LoopEligibility.IsPlayerTurret(caster)) return;

                    float k = LoopFactor("RL_RangedAccuracy");
                    if (k >= 1f) return;

                    __result = 1f - (1f - __result) * k;
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] HitFactorFromShooter postfix failed: " + ex);
                }
            }
        }

        // ─────────── 远程必中: the only literal "never miss" (设计进化's zeroes) ───────────

        /// <summary>
        /// The 远程必中 guarantee of RL_RangedAccuracy. Zeroing the miss coefficient only clears the
        /// SHOOTER factor — the weapon's own accuracy (factorFromEquipment), weather, smoke, target
        /// size and cover are separate factors multiplied in afterwards, so a 0.6-accuracy weapon
        /// would still miss 40% of the time no matter how small the coefficient gets. This postfix
        /// rewrites the finished report instead: every factor to 1, the cover block chance to 0.
        ///
        /// 2026-10-23: the per-loop dangerous tech (弹道锁定) that used to unlock this was deleted
        /// on user request, so the only remaining way in is 设计进化's authored
        /// `zeroes="RL_RangedAccuracy"` — <see cref="DangerousTech.IsZeroed"/> below resolves
        /// through that list (it no longer requires the chain to author its own dangerous tech).
        ///
        /// With that, both rolls in Verb_LaunchProjectile.TryCastShot pass for certain —
        /// Rand.Chance(AimOnTargetChance_IgnoringPosture) and Rand.Chance(PassCoverChance) — which
        /// is what the player asked for ("patch 掉两次 Rand.Chance").
        ///
        /// Deliberately NOT covered: weapons with a forced miss radius (minigun, grenades,
        /// molotovs) branch into their scatter code BEFORE the report is ever built, so no aim
        /// bonus can reach them. They are area weapons, not aimed shots.
        ///
        /// The fields are private on a struct, so they are reached through Harmony's FieldRef
        /// accessors. Tests/Verify.cs pins every name, so a game update that renames one fails the
        /// verifier instead of silently losing the guarantee.
        /// </summary>
        [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
        public static class Patch_ShotReportPerfect
        {
            public static void Postfix(Thing caster, ref ShotReport __result)
            {
                try
                {
                    if (!LoopEligibility.IsSupported(caster as Pawn)) return;

                    ResearchLoopTemplate template = ResearchLoopTemplates.Get("RL_RangedAccuracy");
                    if (template == null || !DangerousTech.IsZeroed(template)) return;

                    // ShotReport is a struct with private fields and no setters, so it is boxed,
                    // rewritten, and unboxed back. That allocates two objects per shot, which is
                    // nothing next to spawning the projectile — and it only happens after an
                    // endgame 100000-point project is finished.
                    object boxed = __result;
                    SetReportField(boxed, "factorFromShooterAndDist", 1f);
                    SetReportField(boxed, "factorFromEquipment", 1f);
                    SetReportField(boxed, "factorFromTargetSize", 1f);
                    SetReportField(boxed, "factorFromWeather", 1f);
                    SetReportField(boxed, "factorFromCoveringGas", 1f);
                    SetReportField(boxed, "offsetFromDarkness", 0f);
                    SetReportField(boxed, "coversOverallBlockChance", 0f);
                    __result = (ShotReport)boxed;
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] HitReportFor postfix failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Field lookups for the ShotReport rewrite, resolved once each. A missing field caches as
        /// null (and is reported once) rather than throwing on every shot.
        /// </summary>
        private static readonly Dictionary<string, FieldInfo> shotReportFields =
            new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        private static void SetReportField(object boxedReport, string fieldName, float value)
        {
            FieldInfo field;
            if (!shotReportFields.TryGetValue(fieldName, out field))
            {
                field = typeof(ShotReport).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(float))
                {
                    Log.ErrorOnce("[ResearchLoops] ShotReport." + fieldName +
                                  " is not a float field any more; 远程必中 cannot force it.", 0x5A180);
                    field = null;
                }
                shotReportFields[fieldName] = field;
            }
            if (field == null) return;

            field.SetValue(boxedReport, value);
        }

        // ─────────────── shared: the multiplier a loop is currently worth ───────────────

        /// <summary>
        /// Current multiplier of one loop, or 1 when it has no completed level. Reads the template
        /// rather than hardcoding anything, and delegates to
        /// <see cref="ResearchLoopPlan.EffectValue"/> so the patch applies exactly the number the
        /// research node and the buff tooltip show — including the player's effect multiplier, the
        /// lower-is-better floor, and the dangerous tech's literal zero.
        /// </summary>
        private static float LoopFactor(string loopName)
        {
            int level = GameComponent_ResearchLoops.GetLevel(loopName);
            if (level <= 0) return 1f;

            ResearchLoopTemplate template = ResearchLoopTemplates.Get(loopName);
            if (template == null) return 1f;

            return ResearchLoopPlan.EffectValue(template, level, ResearchLoopsConfig.BonusScale,
                DangerousTech.IsZeroed(template));
        }

        // ─────────────── skills: one point per completed level, applied on read ───────────────

        /// <summary>
        /// Every work-type loop levels the matching vanilla skill instead of nudging a stat
        /// (user directive: "改成每个给对应等级加一"). The value is applied on read and never
        /// written into the pawn:
        ///
        ///   - nothing to decay, so skill decay is untouched (Mad Skills solves the opposite
        ///     problem — it stops skills falling);
        ///   - no save data beyond the gene, so removing the mod restores the original levels;
        ///   - vanilla clamps at 20, which would make levels 21+ of a 50-level chain dead weight.
        ///     Work-speed stats are linear in level (SkillNeed_BaseBonus), so the clamp is bypassed
        ///     for skills this mod feeds; stats that index a fixed table (SkillNeed_Direct, e.g.
        ///     PlantHarvestYield) read their last entry, i.e. the level-20 value.
        ///
        /// Visibility: the same bonus is mirrored into the 「科技循环」 gene's aptitudes
        /// (ResearchLoopGene), which is what the gene card and the skill panel actually show.
        ///
        /// Cost: this is on a hot path (every stat evaluation reads skill levels), so the bonus is
        /// pre-summed per skill and cached in the game component; the dictionary lookup below is
        /// the only work once the cache is warm.
        /// </summary>
        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevel))]
        public static class Patch_SkillLevel
        {
            public static void Postfix(SkillRecord __instance, bool includeAptitudes, ref int __result)
            {
                if (!includeAptitudes) return;   // also the recursion guard for GetLevel(false)
                ApplySkillBonus(__instance, ref __result);
            }
        }

        /// <summary>
        /// Same value for the UI, otherwise the skill panel would show a different number than the
        /// one the game actually uses (GetLevelForUI has its own body and its own 0..20 clamp).
        /// </summary>
        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.GetLevelForUI))]
        public static class Patch_SkillLevelUI
        {
            public static void Postfix(SkillRecord __instance, bool includeAptitudes, ref int __result)
            {
                if (!includeAptitudes) return;
                ApplySkillBonus(__instance, ref __result);
            }
        }

        /// <summary>
        /// Uncapped skill level: base + every aptitude + this mod's loop bonus.
        ///
        /// Vanilla returns Clamp(levelInt + Aptitude, 0, 20), and the gene carries the bonus AS an
        /// aptitude — so the clamp is exactly what would waste levels 21+ of a 50-level chain. The
        /// value is therefore recomputed without the upper clamp and the larger of the two wins.
        /// Only skills this mod actually feeds are touched.
        /// </summary>
        private static void ApplySkillBonus(SkillRecord record, ref int result)
        {
            try
            {
                if (record == null || record.def == null || record.TotallyDisabled) return;
                if (!LoopEligibility.IsSupported(record.Pawn)) return;

                int bonus = GameComponent_ResearchLoops.GetSkillBonus(record.def.defName);
                if (bonus <= 0) return;

                // Subtract whatever the gene def already contributes before adding the live bonus.
                // The subtraction only applies when the pawn actually carries the gene: a pawn that
                // has not been granted it yet must still get the full bonus, otherwise a fresh
                // recruit would silently lose every level the colony has researched.
                int carried = ResearchLoopGene.HasGene(record.Pawn)
                    ? ResearchLoopGene.AptitudeFor(record.def)
                    : 0;
                int desired = record.GetLevel(false) + record.Aptitude - carried + bonus;

                if (desired > result) result = desired;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[ResearchLoops] skill level postfix failed: " + ex, 0x5A17E);
            }
        }

        // ─────────────── butchery yield: the skill's fractional gain must not be swallowed ───────────────

        /// <summary>
        /// ButcheryFleshEfficiency is now driven by the Cooking skill (+0.025 per level), and
        /// vanilla rounds the products with <c>RoundRandom(base * efficiency)</c> inside the
        /// ButcherProducts iterators — 20 meat at +2.5% rounds straight back to 20.
        ///
        /// The wrapper only ever RAISES a count to the ceiling of the same expression, so it works
        /// no matter where the efficiency came from (skill, another mod, a hediff). It recomputes
        /// the base from the same inputs vanilla uses (MeatAmount / LeatherAmount stats, or the
        /// def's butcherProducts entry), so it cannot invent products that do not belong to the
        /// corpse, and never lowers a value vanilla or another mod already produced more of.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ButcheryYield
        {
            // 屠宰产出取整跟着「技能等级」链走：2026-10-22 起八条技能链合并成 RL_Skills，
            // 它同样每级给烹饪 +1（ButcheryFleshEfficiency 由烹饪等级驱动），所以门槛看它。
            private const string LoopName = "RL_Skills";
            private static int loggedErrors;

            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Pawn), nameof(Pawn.ButcherProducts));
                yield return AccessTools.Method(typeof(Thing), nameof(Thing.ButcherProducts));
            }

            public static bool Prepare()
            {
                foreach (MethodBase method in TargetMethods())
                {
                    if (method == null)
                    {
                        Log.Warning("[ResearchLoops] ButcherProducts not found; the butchery yield loop " +
                                    "will not round up on this game version.");
                        return false;
                    }
                }
                return true;
            }

            public static void Postfix(Thing __instance, float efficiency, ref IEnumerable<Thing> __result)
            {
                try
                {
                    if (__instance == null || __result == null) return;
                    if (GameComponent_ResearchLoops.GetLevel(LoopName) <= 0) return;

                    __result = RaiseToCeiling(__result, __instance, efficiency);
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] ButcherProducts postfix failed: " + ex);
                }
            }

            private static IEnumerable<Thing> RaiseToCeiling(IEnumerable<Thing> source, Thing butchered,
                float efficiency)
            {
                float difficulty = 1f;
                if (Find.Storyteller != null)
                {
                    difficulty = Find.Storyteller.difficulty.butcherYieldFactor;
                }

                foreach (Thing product in source)
                {
                    if (product != null)
                    {
                        try
                        {
                            float baseCount;
                            bool scaledByDifficulty;
                            if (TryBaseCount(butchered, product.def, out baseCount, out scaledByDifficulty))
                            {
                                int raised = Mathf.CeilToInt(baseCount * efficiency);
                                if (scaledByDifficulty)
                                {
                                    raised = GenMath.RoundRandom(raised * difficulty);
                                }
                                if (raised > product.stackCount)
                                {
                                    product.stackCount = raised;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Never let a rounding fix break butchery: log once, keep the product.
                            if (loggedErrors++ == 0)
                            {
                                Log.Error("[ResearchLoops] butchery yield adjustment failed (reported once): " + ex);
                            }
                        }
                    }
                    yield return product;
                }
            }

            /// <summary>
            /// The pre-rounding count vanilla multiplied by <paramref name="efficiency"/>, or false
            /// when this product is not efficiency-scaled (body parts, for instance, are flat).
            /// </summary>
            private static bool TryBaseCount(Thing butchered, ThingDef productDef, out float baseCount,
                out bool scaledByDifficulty)
            {
                baseCount = 0f;
                scaledByDifficulty = false;
                if (butchered == null || productDef == null) return false;

                Pawn pawn = butchered as Pawn;
                if (pawn != null && pawn.RaceProps != null)
                {
                    if (pawn.RaceProps.meatDef == productDef)
                    {
                        baseCount = pawn.GetStatValue(StatDefOf.MeatAmount);
                        return true;
                    }
                    if (pawn.RaceProps.leatherDef == productDef)
                    {
                        baseCount = pawn.GetStatValue(StatDefOf.LeatherAmount);
                        return true;
                    }
                }

                List<ThingDefCountClass> products = butchered.def != null ? butchered.def.butcherProducts : null;
                if (products != null)
                {
                    for (int i = 0; i < products.Count; i++)
                    {
                        if (products[i].thingDef == productDef)
                        {
                            baseCount = products[i].count;
                            // Thing.ButcherProducts also applies the storyteller's butcherYieldFactor
                            // to these, so the raised value has to go through the same roll.
                            scaledByDifficulty = true;
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        // ─────────────── hunger rate: no StatDef in 1.6 (DESIGN §13.4 #4) ───────────────

        /// <summary>
        /// 1.6 removed the hunger-rate StatDef, so the loop scales Need_Food's fall rate directly.
        /// Prisoners are excluded, matching the colonist/slave rule.
        ///
        /// The pawn behind a Need_Food is <c>RimWorld.Need.pawn</c>, which is
        /// <c>protected readonly</c> with no public accessor (verified in 1.6.4871). The
        /// predecessor recovered it by scanning PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive
        /// and matching the Need by reference on every call — an O(pawns x needs) walk inside a
        /// method that pawn AI ticks call repeatedly, i.e. a real late-game performance tax.
        /// A Harmony FieldRef accessor reads the field in O(1) instead. It is built lazily so a
        /// game update that renames the field degrades to "loop disabled + one log line" rather
        /// than throwing while PatchAll runs.
        /// </summary>
        [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.FoodFallPerTickAssumingCategory))]
        public static class Patch_FoodFall
        {
            private static AccessTools.FieldRef<Need, Pawn> pawnRef;
            private static bool pawnRefUnavailable;

            /// <summary>Reads Need.pawn in O(1); null when the field is unavailable on this game version.</summary>
            private static Pawn PawnOf(Need_Food need)
            {
                if (pawnRefUnavailable) return null;
                if (pawnRef == null)
                {
                    try
                    {
                        pawnRef = AccessTools.FieldRefAccess<Need, Pawn>("pawn");
                    }
                    catch (Exception ex)
                    {
                        pawnRefUnavailable = true;
                        Log.Error("[ResearchLoops] Need.pawn accessor unavailable; the hunger-rate loop is " +
                                  "disabled on this game version. (" + ex.Message + ") (reported once)");
                        return null;
                    }
                }
                return pawnRef(need);
            }

            public static void Postfix(Need_Food __instance, ref float __result)
            {
                try
                {
                    if (__instance == null) return;

                    // Cheap gate first: this method is called from pawn AI ticks, so until the loop
                    // has been researched at all there must be no extra work per call.
                    int count = GameComponent_ResearchLoops.GetLevel("RL_FoodFall");
                    if (count <= 0) return;

                    if (!LoopEligibility.IsSupported(PawnOf(__instance))) return;

                    // Same number as the buff tooltip and the research node: RL_FoodFall is a
                    // lower-is-better factor loop, so the floor in LoopMath.ScaledFactor applies
                    // here too — hunger can be slowed but never stopped.
                    __result *= LoopFactor("RL_FoodFall");
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] FoodFallPerTickAssumingCategory postfix failed: " + ex);
                }
            }
        }

        // ─────────────── body part health: no StatDef on the 1.6 path ───────────────

        /// <summary>
        /// Vanilla computes a body part's max HP inside BodyPartDef.GetMaxHealth —
        /// CeilToInt(hitPoints * Pawn.HealthScale) — and there is no StatDef anywhere
        /// on that path, so this loop scales the result directly, the same way
        /// RL_RangedDamage patches Projectile.DamageAmount and RL_FoodFall patches
        /// Need_Food.
        ///
        /// HediffSet.GetPartHealth (health tab, capacities, injury math) reads this
        /// method live, so a completed level reaches every eligible pawn at once and a
        /// loaded save needs no migration. Knock-on effects share the same vanilla
        /// formula: a missing part's bleed rate and pain offset scale with max HP, and
        /// an injured part's efficiency rises with it.
        /// </summary>
        [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
        public static class Patch_BodyPartHealth
        {
            private const string LoopName = "RL_BodyPartHealth";

            public static void Postfix(BodyPartDef __instance, Pawn pawn, ref float __result)
            {
                try
                {
                    // Cheap gate first: until the loop has been researched at all there is
                    // no work per call (this runs per body part, per health-UI refresh).
                    if (GameComponent_ResearchLoops.GetLevel(LoopName) <= 0) return;
                    if (!LoopEligibility.IsLoopTarget(pawn)) return;
                    __result *= LoopFactor(LoopName);
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] BodyPartDef.GetMaxHealth postfix failed: " + ex);
                }
            }
        }

        // ─────────────── melee hit / dodge: past the vanilla curve's cap ───────────────

        /// <summary>
        /// MeleeHitChance / MeleeDodgeChance are the two stats whose vanilla value the mod could
        /// not move far enough: both carry a postProcessCurve that CAPS the final chance (hit
        /// saturates around 98%, dodge around 50%, both at raw 60 skill points), and a hediff
        /// statOffset lands BEFORE that curve — so an additive buff is swallowed the moment the
        /// curve saturates. That is exactly why these two chains were buff-delivered and then did
        /// nothing past ~level 40 (levels 41–50 were pure cost with zero effect).
        ///
        /// 2026-10-23 (user: 「到上限也依旧增加数值」): the two chains are patch-delivered instead.
        /// The bonus is added to the FINAL, post-curve chance the verb actually rolls, so it stacks
        /// on top of the vanilla cap and keeps climbing to a true 100% — the only real ceiling.
        ///
        /// The interception is the verb's own two private resolvers, which read the capped stat and
        /// nothing else:
        ///   - GetNonMissChance: the ATTACKER's hit roll (Rand.Chance must pass to land a hit).
        ///   - GetDodgeChance:   the DEFENDER's dodge roll (Rand.Chance must FAIL to land a hit).
        /// The attacker is __instance.CasterPawn; the defender is the method's own target argument,
        /// bound positionally as __0 (see §8.2 patch 惯用法 — a named bind would break PatchAll on a
        /// game-side parameter rename). TryCastShot passes currentTarget.Thing as that target, so it
        /// is the very same target vanilla reads.
        ///
        /// The value is the SAME number the research node and buff tooltip quote
        /// (<see cref="ResearchLoopPlan.EffectValue"/>, add mode = perLevel × level × effect
        /// slider), divided by 100 because perLevel=1 now means "one percentage point" rather than
        /// "one skill point". Mathf.Clamp01 keeps it a probability — Rand.Chance already treats ≥1
        /// as certain, but the clamped value is what any future reader would see.
        /// </summary>
        [HarmonyPatch(typeof(Verb_MeleeAttack), "GetNonMissChance")]
        public static class Patch_MeleeHitChance
        {
            public static void Postfix(Verb_MeleeAttack __instance, ref float __result)
            {
                try
                {
                    if (__instance == null) return;
                    AddMeleeChance("RL_MeleeHit", __instance.CasterPawn, ref __result);
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] GetNonMissChance postfix failed: " + ex);
                }
            }
        }

        /// <summary>Defender-side twin of <see cref="Patch_MeleeHitChance"/> (see its doc).</summary>
        [HarmonyPatch(typeof(Verb_MeleeAttack), "GetDodgeChance")]
        public static class Patch_MeleeDodgeChance
        {
            public static void Postfix(Verse.LocalTargetInfo __0, ref float __result)
            {
                try
                {
                    Pawn defender = __0.Thing as Pawn;
                    AddMeleeChance("RL_Dodge", defender, ref __result);
                }
                catch (Exception ex)
                {
                    Log.Error("[ResearchLoops] GetDodgeChance postfix failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Adds one loop's post-curve percentage-point bonus to a melee chance. Shared by the hit
        /// and dodge patches; the pawn is whoever the vanilla method already read (attacker for
        /// hit, defender for dodge), so eligibility is checked against that same pawn.
        /// </summary>
        private static void AddMeleeChance(string loopName, Pawn pawn, ref float chance)
        {
            // Cheap gate first: an un-researched loop (the common case) costs one dictionary read.
            int level = GameComponent_ResearchLoops.GetLevel(loopName);
            if (level <= 0) return;
            if (!LoopEligibility.IsLoopTarget(pawn)) return;

            ResearchLoopTemplate template = ResearchLoopTemplates.Get(loopName);
            if (template == null) return;

            float bonus = ResearchLoopPlan.EffectValue(template, level,
                ResearchLoopsConfig.BonusScale, DangerousTech.IsZeroed(template)) * 0.01f;
            if (bonus <= 0f) return;

            chance = Mathf.Clamp01(chance + bonus);
        }
    }
}
