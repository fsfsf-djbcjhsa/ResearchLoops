using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// One ascension's own hediff (2026-10-23, user directive: 「把四个飞升效果单拎出来，健康栏中
    /// 独立于循环科技效果」).
    ///
    /// Until today every ascension effect rode the loop buff: 心胜于物's PsychicEntropyGain ×0 and
    /// 设计进化's capacity offset / ×50 healing lived on the SAME stage as the 29 chain modifiers,
    /// so the health tab showed one line whose tooltip mixed both. Now each ascension has its own
    /// def (see Defs/HediffDefs/ResearchLoopAscension.xml) and its own stage:
    ///
    ///   - 心胜于物 (effect="ascension") — PsychicEntropyGain ×0. Still no patch: every entropy
    ///     source in vanilla multiplies by that stat, so one factor covers psycasts, entropy links
    ///     and anything a mod adds later, and it stays pawn-scoped (enemy psycasts stay expensive).
    ///   - 设计进化 (effect="gene") — capacityOffset on EVERY PawnCapacityDef plus the authored
    ///     &lt;statFactors&gt; (×50 healing / immunity).
    ///   - 合成进化 / 血肉苦弱 — no stat of their own (their work is patches and `zeroes`), so their
    ///     stage carries only the tooltip lines. They still deserve the health line: it is the only
    ///     place that says "this pawn has ascended".
    ///
    /// This is a SPLIT, not a re-balance: vanilla sums the stages of every hediff on a pawn, so the
    /// numbers on the stat card are identical to the pre-split build.
    ///
    /// The def -> ascension binding is authored (`hediffDef=` on the &lt;Dangerous&gt; element) and
    /// resolved through <see cref="AscensionBuff.InfoForDef"/>.
    /// </summary>
    public class Hediff_Ascension : Hediff
    {
        [Unsaved] private int cachedVersion = -1;
        [Unsaved] private HediffStage cachedStage;
        [Unsaved] private string cachedTip;
        [Unsaved] private string cachedTipMech;

        private DangerousTechInfo Info
        {
            get { return AscensionBuff.InfoForDef(def); }
        }

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
                // A mech reads the shorter list: `mechs="false"` lines (immunity, healing) would be
                // lies on a machine. Same rule as the loop buff.
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

        private HediffStage BuildStage()
        {
            List<StatModifier> factors = null;
            List<PawnCapacityModifier> capMods = null;

            DangerousTechInfo info = Info;
            if (info != null)
            {
                // 心胜于物: no entropy, ever.
                if (info.IsEffect(DangerousTech.EffectAscension))
                {
                    factors = new List<StatModifier>();
                    factors.Add(new StatModifier { stat = StatDefOf.PsychicEntropyGain, value = 0f });
                }

                // 设计进化: +100% to every capacity vanilla knows about (「操作能力之类的全部加100%」).
                if (info.capacityOffset > 0f) capMods = AddCapacityMods(capMods, info.capacityOffset);

                // 设计进化: ×50 healing / immunity, authored in <statFactors>.
                if (info.statFactors != null)
                {
                    for (int i = 0; i < info.statFactors.Count; i++)
                    {
                        DangerousStatFactor authored = info.statFactors[i];
                        if (authored == null || string.IsNullOrEmpty(authored.stat)) continue;

                        StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(authored.stat);
                        if (stat == null)
                        {
                            Log.ErrorOnce("[ResearchLoops] ascension stat '" + authored.stat +
                                          "' not found; that multiplier is missing from the hediff.",
                                authored.stat.GetHashCode());
                            continue;
                        }

                        if (factors == null) factors = new List<StatModifier>();
                        factors.Add(new StatModifier { stat = stat, value = authored.value });
                    }
                }
            }

            // painFactor / totalBleedFactor MUST be 1 here: HediffStage's C# field defaults are not
            // the XML defaults, and a 0 would silently annihilate the pawn's pain and bleeding for
            // an ascension that never asked for it (those zeros belong to 血肉苦弱's `zeroes` and
            // are applied by the loop buff, not by this stage).
            return new HediffStage
            {
                statFactors = factors,
                capMods = capMods ?? new List<PawnCapacityModifier>(),
                painFactor = 1f,
                totalBleedFactor = 1f,
                becomeVisible = true
            };
        }

        /// <summary>
        /// Adds one offset to every PawnCapacityDef vanilla knows about (操作/移动/视觉/听觉/呼吸/
        /// 意识/代谢…). All capacities on purpose: a hand-picked subset would silently miss whichever
        /// capacity a mod adds later.
        /// </summary>
        private static List<PawnCapacityModifier> AddCapacityMods(List<PawnCapacityModifier> mods, float offset)
        {
            if (mods == null) mods = new List<PawnCapacityModifier>();

            List<PawnCapacityDef> capacities = DefDatabase<PawnCapacityDef>.AllDefsListForReading;
            for (int i = 0; i < capacities.Count; i++)
            {
                PawnCapacityDef capacity = capacities[i];
                if (capacity == null) continue;
                mods.Add(new PawnCapacityModifier { capacity = capacity, offset = offset });
            }
            return mods;
        }

        /// <summary>
        /// The ascension's body text: the authored &lt;buffLines&gt; for THIS tech, nothing else —
        /// same "effect only, no header, no name, no Lv.N" voice as the loop buff.
        /// </summary>
        private string BuildTip(bool forMech)
        {
            Localization.Sync();
            DangerousTechInfo info = Info;
            if (info == null || info.buffLines == null) return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < info.buffLines.Count; i++)
            {
                DangerousBuffLine line = info.buffLines[i];
                if (line == null || string.IsNullOrEmpty(line.text)) continue;
                if (forMech && !line.forMech) continue;
                sb.AppendLine(ResearchLoopPlan.Pick(line.text, line.textEn));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
