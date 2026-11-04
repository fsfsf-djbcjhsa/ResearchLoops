using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The visible carrier of every buff-delivered loop: ONE hediff on every free colonist and
    /// slave, modeled on RimSkyBlock's empire bonus (RSB_Hediff_EmpireEffects / Hediff_DynamicStage).
    ///
    /// The gene could only ever carry skill loops — the one vanilla mechanism a gene feeds is
    /// SkillRecord.Aptitude. The stat chains ride this buff hediff, and a stat contribution
    /// nobody can see on the pawn is indistinguishable from a bug (user directive: 「把无法在基因中显示的
    /// 效果都仿照空岛战争的帝国加成效果一样写成 buff」). This hediff is that buff:
    ///
    ///   - <see cref="CurStage"/> is rebuilt from the colony's current levels whenever they change,
    ///     and the stat offsets/factors vanilla already applies from hediff stages ARE the loop
    ///     bonus. No patch runs on any stat evaluation hot path.
    ///   - <see cref="TipStringExtra"/> is a bare list of active effects — one line per chain, no
    ///     header, no names, no level numbers (user directive). See <see cref="BuildTip"/>.
    ///   - The two patch-delivered chains (ranged damage, hunger rate) have no pawn stat to ride,
    ///     so they appear in that list as plain effect lines while their Harmony patches keep
    ///     working.
    ///
    /// The cache key is GameComponent_ResearchLoops.BuffVersion: every completed level and every
    /// (re)load bumps it, so a stale stage can never outlive the research that produced it.
    /// </summary>
    public class Hediff_ResearchLoop : Hediff
    {
        [Unsaved] private int cachedVersion = -1;
        [Unsaved] private HediffStage cachedStage;
        [Unsaved] private string cachedTip;
        [Unsaved] private string cachedTipMech;

        /// <summary>
        /// The living stage. base.CurStage must exist (the def authors one visible stage), then
        /// the real modifiers are built from the researched levels — exactly the dynamic-stage
        /// pattern RimSkyBlock's Hediff_DynamicStage uses for its reinforcement buff.
        /// </summary>
        public override HediffStage CurStage
        {
            get
            {
                if (base.CurStage == null) return null;
                RefreshCache();
                return cachedStage;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                RefreshCache();
                // A mech reads a shorter list: the biological and psyfocus lines would be lies on
                // a machine (see MechIrrelevantLoops). Same cached string, two flavours.
                bool mech = pawn != null && pawn.RaceProps != null && pawn.RaceProps.IsMechanoid;
                string tip = mech ? cachedTipMech : cachedTip;
                return tip ?? base.TipStringExtra;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ResetCache();
        }

        public override void PostMake()
        {
            base.PostMake();
            ResetCache();
        }

        private void ResetCache()
        {
            cachedVersion = -1;
            cachedStage = null;
            cachedTip = null;
            cachedTipMech = null;
        }

        private void RefreshCache()
        {
            int version = GameComponent_ResearchLoops.CurrentBuffVersion;
            if (cachedStage != null && cachedVersion == version) return;
            cachedStage = BuildStage();
            cachedTip = BuildTip(false);
            cachedTipMech = BuildTip(true);
            cachedVersion = version;
        }

        /// <summary>
        /// One stage collecting every active buff loop's modifiers. factor loops become
        /// statFactors (multiply), add loops become statOffsets (flat addition) — the same split
        /// the retired StatPart made, so the numbers on the stat card do not change.
        ///
        /// Two loops have no StatDef to ride at all (1.6 has no PainFactor and no bleeding-rate
        /// stat), so they name a stage FIELD instead — <see cref="ResearchLoopTemplate.stageField"/>.
        /// Those land on the same stage: HediffStage.painFactor is what Hediff.PainFactor returns
        /// (and HediffSet.PainTotal multiplies every wound's pain by it), HediffStage.totalBleedFactor
        /// is multiplied into HediffSet.BleedRateTotal. Both are therefore global multipliers on the
        /// pawn's existing injuries, which is exactly what the stat-mounted loops cannot express.
        /// </summary>
        private static HediffStage BuildStage()
        {
            List<StatModifier> offsets = null;
            List<StatModifier> factors = null;
            float painFactor = 1f;
            float totalBleedFactor = 1f;

            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.delivery != "buff") continue;

                int level = GameComponent_ResearchLoops.GetLevel(template.defName);
                if (level <= 0) continue;

                if (template.UsesStageField)
                {
                    // Stage fields are multiplicative, so several loops on the same field would
                    // compound; today each field has exactly one owner (pinned by Tests/Verify).
                    // ValueFor (not EffectValue directly) so an authored zero reaches this path
                    // too: since 2026-10-23 painFactor 0 / totalBleedFactor 0 come from 血肉苦弱's
                    // `zeroes` (the per-loop dangerous techs that used to set them are deleted).
                    float value = ValueFor(template, level);
                    if (template.stageField == ResearchLoopTemplate.StagePainFactor) painFactor *= value;
                    else if (template.stageField == ResearchLoopTemplate.StageTotalBleedFactor) totalBleedFactor *= value;
                    continue;
                }

                if (template.targetStats == null) continue;

                foreach (string statName in template.targetStats)
                {
                    if (string.IsNullOrEmpty(statName)) continue;
                    StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statName);
                    if (stat == null) continue;

                    StatModifier modifier = new StatModifier { stat = stat, value = ValueFor(template, level) };
                    if (template.mode == LoopMath.ModeAdd)
                    {
                        if (offsets == null) offsets = new List<StatModifier>();
                        offsets.Add(modifier);
                    }
                    else
                    {
                        if (factors == null) factors = new List<StatModifier>();
                        factors.Add(modifier);
                    }
                }
            }

            // Ascension effects are NOT built here any more (2026-10-23 split, user directive
            // 「把四个飞升效果单拎出来，健康栏中独立于循环科技效果」): 心胜于物's
            // PsychicEntropyGain ×0 and 设计进化's capacity offset / ×50 healing now live on their
            // OWN hediffs (Hediff_Ascension, swept by AscensionBuff), so this stage is chains only
            // and the health tab shows "循环科技" and each researched "飞升" as separate lines.
            // Vanilla sums every hediff stage on a pawn, so the numbers on the stat card do not move.
            return new HediffStage
            {
                statOffsets = offsets,
                statFactors = factors,
                capMods = new List<PawnCapacityModifier>(),
                painFactor = painFactor,
                totalBleedFactor = totalBleedFactor,
                becomeVisible = true
            };
        }

        /// <summary>
        /// This loop's own contribution at a level, from the single source of truth
        /// (<see cref="ResearchLoopPlan.EffectValue"/>): the authored clamp is applied there to the
        /// loop's own value, not to the pawn's total, because a hediff stage cannot see other stat
        /// sources without recursing into the very statistic it is building.
        ///
        /// The player's effect multiplier and the lower-is-better floor live in that same call,
        /// which is why the buff can never drive a stat to zero even at the maximum setting —
        /// unless <see cref="DangerousTech.IsZeroed"/> says the chain is zeroed. Since 2026-10-23
        /// that only happens through a standalone ascension's `zeroes` list (血肉苦弱 covers the
        /// rest/food/joy/pain/bleed chains; the per-loop dangerous techs were deleted).
        /// </summary>
        private static float ValueFor(ResearchLoopTemplate template, int level)
        {
            return ResearchLoopPlan.EffectValue(template, level, ResearchLoopsConfig.BonusScale,
                DangerousTech.IsZeroed(template));
        }

        /// <summary>
        /// The buff's body text: one line per active chain, and nothing else. Built twice — the
        /// organic list and a shorter mech list (loops authored mechApplies="false" are skipped,
        /// because a mech has no hunger, rest, joy, pain, bleeding or psyfocus and bandwidth lives
        /// on the mechanitor) — since 合成进化 puts this same buff on colony mechs.
        ///
        /// User directive: 「效果不要加说明，只用填效果，不要名称」 — so there is no header, no
        /// concept name, no "Lv.N", no delivery marker and no footer. Each line is exactly what
        /// <see cref="ResearchLoopPlan.EffectMagnitude"/> renders, i.e. the same string the
        /// research node quotes, so the buff and the research panel can never disagree.
        ///
        /// Skill chains stay out on purpose: they are delivered by the 「科技循环」 gene, not by
        /// this hediff, and two of them (mining / deep drill) render the identical text
        /// "采矿技能 +N" — listing them here would read as a duplicated line rather than as two
        /// chains. The gene card and the skill panel are where those show up. Passion chains stay
        /// out too: they are written straight into SkillRecord.passion and shown by the skill
        /// panel's own flames, so a buff line would duplicate what the flames already display.
        /// </summary>
        private static string BuildTip(bool forMech)
        {
            Localization.Sync();
            StringBuilder sb = new StringBuilder();
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.delivery == "skill" || template.delivery == "passion") continue;
                if (forMech && !template.mechApplies) continue;

                int level = GameComponent_ResearchLoops.GetLevel(template.defName);
                if (level <= 0) continue;

                // A chain an ascension has FULLY superseded is not listed (user directive
                // 2026-10-23: 「有进阶的初级不显示」). Once 血肉苦弱 zeroes 疼痛/流血/娱乐/疲劳/饥饿,
                // those five lines leave this tooltip and only the ascension's own line remains;
                // same for 设计进化 and RL_RangedAccuracy. The zero itself still applies — ValueFor
                // keeps feeding ×0 into the stage and into the patches — so this hides TEXT only,
                // never the effect.
                if (DangerousTech.IsZeroed(template)) continue;

                sb.AppendLine(ResearchLoopPlan.EffectMagnitude(template, level,
                    ResearchLoopsConfig.BonusScale, false));
            }

            // Ascension lines are NOT appended here any more: since 2026-10-23 each ascension has
            // its own hediff (Hediff_Ascension) which quotes its own <buffLines>, so the health tab
            // separates 「循环科技」 from 「飞升」. See AscensionBuff.
            return sb.ToString().TrimEnd();
        }
    }
}
