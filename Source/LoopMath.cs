using System;
using System.Globalization;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Pure math for research loops. Deliberately has NO Verse/UnityEngine dependency so the
    /// verification project can compile this single file and unit-test it without loading
    /// Assembly-CSharp.
    ///
    /// Only two modes exist (DESIGN §6.3): the "fixed amount" and "offset + clamp" families from
    /// the design table are both plain addition, so they collapse into <see cref="ModeAdd"/>.
    /// </summary>
    public static class LoopMath
    {
        /// <summary>Per-level factor, multiplied onto the base value per level. 1.05 => x1.05^level.</summary>
        public const string ModeFactor = "factor";

        /// <summary>Per-level flat addition. value += perLevel * level.</summary>
        public const string ModeAdd = "add";

        /// <summary>Quality-tier mode: the level buys guaranteed quality tiers.</summary>
        public const string ModeTiers = "tiers";

        /// <summary>
        /// Cost-curve tier names, authored per loop as the `costTier` attribute. A missing or
        /// unrecognised value reads as <see cref="TierNormal"/>, so an un-authored chain keeps the
        /// curve the mod has always shipped and an old template cannot break the math.
        /// </summary>
        public const string TierNormal = "normal";
        public const string TierStrong = "strong";
        public const string TierPower = "power";

        /// <summary>
        /// The three cost curves (2026-10-22, user directive: 一般 / 较强 / 强力).
        /// Each is the fraction of baseCost added PER LEVEL on top of level 1:
        ///   L(k) = base * (1 + slope * (k - 1))
        /// so level 1 always costs exactly baseCost on every tier — only the escalation differs.
        ///
        ///   normal  +50 %/级   -> L50 = 25.5x base, 满链(50级) 662.5x base
        ///   strong  +100 %/级  -> L50 = 50x   base, 满链(50级) 1275x  base
        ///   power   +150 %/级  -> L50 = 74.5x base, 满链(50级) 1887.5x base
        ///
        /// Deliberately linear, not geometric: a compounding curve would put level 50 of a power
        /// chain in the billions. Balance is tuned with baseCost (the entry price, authored per
        /// chain); the slope only decides how fast the late levels bite.
        /// </summary>
        public const float CostSlopeNormal = 0.5f;
        public const float CostSlopeStrong = 1f;
        public const float CostSlopePower = 1.5f;

        /// <summary>
        /// Maps a tier name to its slope. Unknown, null or empty reads as
        /// <see cref="CostSlopeNormal"/> — the "degrade to the shipped curve" rule.
        /// </summary>
        public static float CostSlope(string tier)
        {
            if (string.Equals(tier, TierStrong, StringComparison.OrdinalIgnoreCase)) return CostSlopeStrong;
            if (string.Equals(tier, TierPower, StringComparison.OrdinalIgnoreCase)) return CostSlopePower;
            return CostSlopeNormal;
        }

        /// <summary>
        /// Highest level a single loop may ever report. A corrupted counter (negative, or a
        /// runaway value from an old save / hand-edited file) must not be able to deliver a
        /// multiplier that overflows a stat to infinity. 1000 levels is far above the longest
        /// authored chain (100) and still leaves headroom for a cost-scale of 10 on the
        /// research bill.
        /// </summary>
        public const int LevelCeiling = 1000;

        /// <summary>
        /// Hard floor for every lower-is-better factor. Without it, a 50-level chain at 5 %/level
        /// already sits at x0.077, and scaling that rate up drove IncomingDamageFactor to
        /// x0.0000006 — i.e. literal immunity (reported in game: 「拉满之后承伤系数能到 0」).
        /// 0.05 = at most 95 % reduction: the strongest the loop may ever get, and never zero.
        /// </summary>
        public const float FactorFloor = 0.05f;

        /// <summary>
        /// Player-facing bounds of the effect multiplier, shown as "xN" in the settings page.
        /// Linear on the EFFECT (0.1 = a tenth of the effect, 5 = five times), NOT on the per-level
        /// rate: on a compounding curve the rate form is exponential, and scaling the rate by 5
        /// turned the 50-level x0.077 incoming-damage factor into x0.0000006 — the in-game report
        /// 「拉满之后承伤系数能到 0」. With the linear form the wide 0.1–5 range is safe again.
        /// Lives here (not in the settings class) so the verifier can compile and assert it.
        /// </summary>
        public const float EffectScaleMin = 0.1f;
        public const float EffectScaleMax = 5f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Clamps a level count into [0, <see cref="LevelCeiling"/>]. Used on every counter read
        /// and write so a poisoned save cannot reach the math at all.
        /// </summary>
        public static int SanitizeLevel(int level)
        {
            if (level < 0) return 0;
            if (level > LevelCeiling) return LevelCeiling;
            return level;
        }

        /// <summary>
        /// Returns <paramref name="value"/>, or <paramref name="fallback"/> when it is NaN or
        /// infinite. Every public member below routes its inputs through this, so one bad
        /// authored number degrades to "no change" instead of NaN poisoning every stat that
        /// reads the loop (NaN propagates silently through vanilla stat aggregation).
        /// </summary>
        public static float Safe(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value;
        }

        /// <summary>
        /// Compounding multiplier for 'factor' mode: (1 + perLevel)^level, or (1 - perLevel)^level
        /// when <paramref name="lowerIsBetter"/> (e.g. perLevel 0.05 => x0.95^level).
        /// Returns 1 when level &lt;= 0, i.e. an unresearched loop never changes a value.
        /// </summary>
        public static float Factor(int level, float perLevel, bool lowerIsBetter)
        {
            if (level <= 0) return 1f;
            double rate = lowerIsBetter ? 1.0 - Safe(perLevel, 0f) : 1.0 + Safe(perLevel, 0f);
            // A degenerate rate (perLevel >= 1 on a lower-is-better loop) would fold the stat
            // towards zero or a sign flip; the safe reading is "no change".
            if (rate <= 0.0) return 1f;
            double result = Math.Pow(rate, level);
            if (double.IsInfinity(result)) return float.MaxValue;
            return Safe((float)result, 1f);
        }

        /// <summary>Flat addition for 'add' mode: perLevel * level.</summary>
        public static float Flat(int level, float perLevel)
        {
            if (level <= 0) return 0f;
            return Safe(Safe(perLevel, 0f) * level, 0f);
        }

        /// <summary>
        /// 'factor' mode with the player's effect multiplier applied the SAFE way: linearly on the
        /// distance from 1, not on the per-level rate.
        ///
        ///   x1.41 at scale 2 => 1 + (1.41 - 1) * 2 = x1.82      (twice the effect)
        ///   x0.077 at scale 2 => 1 + (0.077 - 1) * 2 = -0.85    => clamped to FactorFloor
        ///
        /// Scaling the rate instead is what produced the in-game report 「拉满之后承伤系数能到 0」:
        /// 0.05 * 5 = 0.25 per level, 0.75^50 = 6e-7. The floor makes zero unreachable no matter
        /// what the setting, the template or a corrupted save says.
        /// </summary>
        public static float ScaledFactor(int level, float perLevel, bool lowerIsBetter, float scale)
        {
            if (level <= 0) return 1f;

            float baseFactor = Factor(level, perLevel, lowerIsBetter);
            float s = Safe(scale, 1f);
            if (s <= 0f) s = 1f;

            float scaled = 1f + (baseFactor - 1f) * s;
            if (float.IsNaN(scaled) || float.IsInfinity(scaled)) return 1f;

            // Lower-is-better stats can never be driven to zero (immunity) or negative.
            if (lowerIsBetter && scaled < FactorFloor) return FactorFloor;
            return scaled;
        }

        /// <summary>
        /// Research cost of one loop level: base * (1 + slope*(k-1)), i.e. exactly
        /// base at k=1 and 0 for k&lt;=0 (nothing completed). The slope is the chain's cost tier
        /// (<see cref="CostSlopeNormal"/> and up — see the tier table above). Also serves as the
        /// "consumed" cost of a completed level, because vanilla's FinishProject stores the
        /// finished level's cost as raw progress (see ResearchLoopPlan.DisplayProgress).
        /// </summary>
        public static float LevelCost(float baseCost, int level, float slope)
        {
            if (level <= 0) return 0f;
            int safeLevel = SanitizeLevel(level);
            float nominal = Safe(baseCost, 0f);
            float safeSlope = Safe(slope, CostSlopeNormal);
            if (safeSlope < 0f) safeSlope = CostSlopeNormal;
            // Floor to whole research points: at the authored baseCost and integer slopes every
            // level is already an integer, but the player's cost multiplier (0.1–10) can produce
            // fractional points (e.g. 750 * 0.11 = 82.5). Research points are counted as integers
            // in the panel, so flooring here keeps the def, the projection and the progress
            // arithmetic all on whole numbers (no rounding drift between surfaces).
            float raw = nominal * (1f + safeSlope * (safeLevel - 1));
            float floored = (float)Math.Floor(raw);
            return Safe(floored, 0f);
        }

        /// <summary>Hard clamp. Null bounds are left open.</summary>
        public static float Clamp(float value, float? min, float? max)
        {
            if (min.HasValue && value < min.Value) value = min.Value;
            if (max.HasValue && value > max.Value) value = max.Value;
            return value;
        }

        /// <summary>
        /// Guaranteed quality tiers gained at a level (DESIGN §13.2 / P4): floor(perLevel * level).
        /// The fractional remainder is rolled separately by the patch, so this is the floor part.
        /// </summary>
        public static int QualityTiers(int level, float perLevel)
        {
            if (level <= 0) return 0;
            double tiers = Safe(perLevel, 0f) * SanitizeLevel(level);
            if (double.IsNaN(tiers) || tiers <= 0.0) return 0;
            if (tiers > LevelCeiling) return LevelCeiling;
            return (int)Math.Floor(tiers);
        }

        /// <summary>
        /// The per-level value the stat is moved by, in the mode's own units
        /// (a multiplier, or an additive offset). 1/0 when level &lt;= 0.
        /// </summary>
        public static float PerLevelValue(int level, string mode, float perLevel, bool lowerIsBetter)
        {
            if (level <= 0) return 0f;
            if (mode == ModeAdd) return Flat(level, perLevel);
            return Factor(level, perLevel, lowerIsBetter);
        }

        /// <summary>
        /// Invariant-culture "%"-style magnitude used in the tooltip explanation: the per-level
        /// rate as a percentage string, e.g. 0.05 => "5%".
        /// </summary>
        public static string PerLevelPercent(float perLevel)
        {
            double pct = Math.Round(Safe(perLevel, 0f) * 100.0, 3);
            return pct.ToString("0.###", Inv) + "%";
        }

        /// <summary>
        /// Human-readable tooltip magnitude for a given level, the same text the StatPart shows:
        ///   factor          => "x1.41"
        ///   add             => "+0.15"
        ///   factor + clamp  => "x1.41 (钳制在 0.50)"
        /// Returns null when level &lt;= 0 so the caller can omit the line entirely.
        /// </summary>
        public static string Explanation(int level, string mode, float perLevel, bool lowerIsBetter,
            float? clampMin, float? clampMax)
        {
            if (level <= 0) return null;

            if (mode == ModeAdd)
            {
                string add = "+" + Flat(level, perLevel).ToString("0.###", Inv);
                return add + ClampSuffix(clampMin, clampMax);
            }

            string factor = "x" + Factor(level, perLevel, lowerIsBetter).ToString("0.####", Inv);
            return factor + ClampSuffix(clampMin, clampMax);
        }

        /// <summary>
        /// English output for the clamp suffix (2026-10-23 bilingual support). Set by
        /// <see cref="Localization.Sync"/>; this file must stay free of RimWorld types so the
        /// verifier can compile it, which is why the flag is pushed in rather than read here.
        /// </summary>
        public static bool English;

        private static string ClampSuffix(float? clampMin, float? clampMax)
        {
            if (!clampMin.HasValue && !clampMax.HasValue) return string.Empty;
            if (clampMin.HasValue && clampMax.HasValue)
            {
                string lo = clampMin.Value.ToString("0.###", Inv);
                string hi = clampMax.Value.ToString("0.###", Inv);
                return English ? " (clamped " + lo + " to " + hi + ")" : " (钳制在 " + lo + "–" + hi + ")";
            }
            if (clampMax.HasValue)
            {
                string hi = clampMax.Value.ToString("0.###", Inv);
                return English ? " (max " + hi + ")" : " (上限 " + hi + ")";
            }
            string loOnly = clampMin.Value.ToString("0.###", Inv);
            return English ? " (min " + loOnly + ")" : " (下限 " + loOnly + ")";
        }
    }
}
