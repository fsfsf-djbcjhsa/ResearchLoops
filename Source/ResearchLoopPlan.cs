using System;
using System.Collections.Generic;
using System.Globalization;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Pure, single owner of "which level is a loop currently on, and what does the def say
    /// about it". Same contract as LoopMath: no Verse/UnityEngine dependency, so the
    /// verification project compiles this file in directly and asserts the rules.
    ///
    /// Three consumers must agree on this state and all read it from here:
    ///   - PatchOperation_GenerateLoopDefs writes the level-1 state into the generated XML,
    ///   - ResearchLoopProgress re-applies it to the def after each level-up and on game load,
    ///   - the ResearchProjectDef.ProgressReal postfix derives the per-level progress bar.
    ///
    /// Level model: a loop is ONE ResearchProjectDef forever. 'completedLevels' (the counter
    /// in GameComponent_ResearchLoops) decides the state; the def always offers level
    /// completedLevels+1 (clamped at maxLevel). The def is never destroyed, so the next level
    /// is "research this same node again" — that is the fix for the old "no next level" defect
    /// where level defs were supposed to appear but never did.
    /// </summary>
    public static class ResearchLoopPlan
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The level the def is currently offering: completed + 1, clamped to maxLevel.</summary>
        public static int NextLevel(ResearchLoopTemplate t, int completedLevels)
        {
            if (t == null || completedLevels < 0) return 1;
            return Math.Min(completedLevels + 1, t.maxLevel);
        }

        /// <summary>True when the chain is exhausted: no further level exists.</summary>
        public static bool ChainComplete(ResearchLoopTemplate t, int completedLevels)
        {
            return t == null || completedLevels >= t.maxLevel;
        }

        /// <summary>
        /// The def's baseCost while 'completedLevels' levels are done: the cost of the level
        /// being offered. Deliberately stays at the FINAL level's cost afterwards, so a
        /// completed chain reads as finished (progress == cost) instead of becoming free.
        /// <paramref name="costScale"/> is the player's global cost multiplier (default 1 =
        /// authored values); every caller that can be on-screen passes the live one so the
        /// research panel, the def and the progress arithmetic always agree.
        /// </summary>
        public static float CostForLevel(ResearchLoopTemplate t, int completedLevels, float costScale = 1f, float bonusScale = 1f)
        {
            return LevelCost(t, NextLevel(t, completedLevels), costScale, bonusScale);
        }

        /// <summary>Nominal research cost of one level, from the authored baseCost.</summary>
        public static float LevelCost(ResearchLoopTemplate t, int level, float costScale = 1f, float bonusScale = 1f)
        {
            if (t == null || level <= 0) return 0f;
            float scale = LoopMath.Safe(costScale, 1f);
            if (scale <= 0f) scale = 1f;
            // Item-quality loop: its benefit is a fixed tier count, so the effect multiplier does
            // nothing to the effect and instead makes the research cheaper (cost ÷ multiplier).
            if (t.costInverseEffectScale)
            {
                float bs = LoopMath.Safe(bonusScale, 1f);
                if (bs <= 0f) bs = 1f;
                scale /= bs;
            }
            return LoopMath.LevelCost(t.baseCost * scale, level, t.CostSlope);
        }

        /// <summary>
        /// Effective progress towards the level being researched. Raw progress is never reset
        /// (ResearchManager.progress is private and shared with vanilla bookkeeping), and
        /// vanilla's FinishProject stores the finished level's cost there. So the consumed
        /// cost of the last completed level is subtracted on read instead:
        ///   raw = consumed + thisLevelProgress.
        /// This both gives each level a fresh bar and makes IsFinished (ProgressReal >= Cost)
        /// fire exactly when the level's own nominal cost has been paid.
        /// An exhausted chain reports at least its final cost, so it reads as finished.
        /// <paramref name="costScale"/> must be the SAME multiplier that projected the def's
        /// baseCost (vanilla stores the scaled cost as raw progress), which is why the research
        /// flow passes the live one from <see cref="ResearchLoopsConfig"/>.
        /// </summary>
        public static float DisplayProgress(ResearchLoopTemplate t, int completedLevels, float rawProgress,
            float costScale = 1f, float bonusScale = 1f)
        {
            if (t == null) return rawProgress;
            if (ChainComplete(t, completedLevels))
            {
                return Math.Max(rawProgress, LevelCost(t, t.maxLevel, costScale, bonusScale));
            }
            return Math.Max(0f, rawProgress - LevelCost(t, completedLevels, costScale, bonusScale));
        }

        /// <summary>
        /// True when the mod should render English text (2026-10-23, user directive 「加入英文版，
        /// 除语言为中文外其余语言对应均为英文」). Chinese — simplified OR traditional — keeps the
        /// authored Chinese; every other language falls back to the English fields.
        ///
        /// Set by the Verse-side entry points (<see cref="Localization"/>), because this file must
        /// stay free of RimWorld types so the verifier can compile it. The verifier never sets it,
        /// so it always asserts the Chinese copy.
        /// </summary>
        public static bool English;

        /// <summary>Chinese when we are in a Chinese language, English otherwise (empty falls back).</summary>
        public static string Pick(string chinese, string english)
        {
            return English && !string.IsNullOrEmpty(english) ? english : chinese;
        }

        /// <summary>
        /// The research node's full text for a STANDALONE dangerous tech (飞升): the authored blurb,
        /// then one line per authored effect (buffLines), then the prerequisite line. That last one
        /// is new since 2026-11-04: the gate moved off the def's &lt;prerequisites&gt; onto this mod's
        /// own level check, so vanilla's "Prerequisites:" list is gone and the node has to state the
        /// requirement itself — otherwise a greyed node gives no reason at all. Shared by the
        /// load-time generator (PatchOperation_GenerateLoopDefs) and the runtime refresh
        /// (ResearchLoopProgress) so both pick the language the same way — the generator can run
        /// before the language DB is ready (Player.log: "generated ... defs" precedes "updating
        /// labels for simplified chinese"), so the baked text must be re-picked after load or a
        /// Chinese game shows English (2026-10-25).
        ///
        /// <paramref name="prereqName"/> turns a prerequisite defName into that chain's display name.
        /// It is a delegate because this file must stay Verse-free (the verifier compiles it in) and
        /// the name lives in the template table. Null or an unknown defName falls back to the defName,
        /// so a typo is visible on the node instead of silent.
        /// </summary>
        public static string StandaloneNodeText(DangerousTechInfo info, Func<string, string> prereqName = null)
        {
            if (info == null) return string.Empty;

            var sb = new System.Text.StringBuilder(Pick(info.description, info.descriptionEn));

            if (info.buffLines != null)
            {
                for (int i = 0; i < info.buffLines.Count; i++)
                {
                    DangerousBuffLine line = info.buffLines[i];
                    if (line == null || string.IsNullOrEmpty(line.text)) continue;

                    string text = Pick(line.text, line.textEn);
                    if (string.IsNullOrEmpty(text)) continue;

                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(text);
                }
            }

            string gate = AscensionGateLine(info, prereqName);
            if (!string.IsNullOrEmpty(gate))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(gate);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The standalone ascension's research gate, as a pure rule (2026-11-04, user
        /// 「飞升的话要求前置技能10级」): EVERY chain listed in `prerequisite` must have completed at
        /// least `prereqLevel` levels.
        ///
        /// Levels come in through <paramref name="levelOf"/> — the game passes
        /// GameComponent_ResearchLoops.GetLevel, the verifier passes whatever counter it wants to
        /// feed. Nothing here consults vanilla's IsFinished, and that is the point: for a loop def
        /// IsFinished compares ResearchManager's stored progress against the baseCost this mod
        /// re-projects on every level-up, so "finished" is not the same statement as "this chain has
        /// N levels" (the user's report 「游戏里飞升只要 2 级就能研究」 is that comparison leaking into
        /// the prerequisite test).
        ///
        /// Fails closed: no info, no prereqLevel, no prerequisites, no way to read levels, or an
        /// unknown chain (GetLevel returns 0) all keep the node locked. A gate that cannot be
        /// evaluated is never opened.
        /// </summary>
        public static bool AscensionLevelsMet(DangerousTechInfo info, Func<string, int> levelOf)
        {
            if (info == null || info.prereqLevel <= 0) return false;
            if (info.prerequisites == null || info.prerequisites.Count == 0) return false;
            if (levelOf == null) return false;

            for (int i = 0; i < info.prerequisites.Count; i++)
            {
                if (levelOf(info.prerequisites[i]) < info.prereqLevel) return false;
            }
            return true;
        }

        /// <summary>
        /// The node's requirement line, built from the same authored data as
        /// <see cref="AscensionLevelsMet"/> so the text and the gate cannot drift: the prerequisite
        /// chains by name plus the level each of them needs.
        /// </summary>
        public static string AscensionGateLine(DangerousTechInfo info, Func<string, string> prereqName)
        {
            if (info == null || info.prereqLevel <= 0) return string.Empty;
            if (info.prerequisites == null || info.prerequisites.Count == 0) return string.Empty;

            var names = new System.Text.StringBuilder();
            for (int i = 0; i < info.prerequisites.Count; i++)
            {
                string defName = info.prerequisites[i];
                if (names.Length > 0) names.Append(English ? ", " : "、");

                string shown = prereqName != null ? prereqName(defName) : null;
                names.Append(string.IsNullOrEmpty(shown) ? defName : shown);
            }

            return English
                ? "Requires: " + names + " — each chain at level " + info.prereqLevel.ToString(Inv) + "."
                : "需要：" + names + " 每条练到 " + info.prereqLevel.ToString(Inv) + " 级";
        }

        /// <summary>
        /// One row of the ascension's prerequisite block: the text the panel shows, and whether that
        /// chain has reached the gate. The Verse side colours <see cref="Met"/> green and everything
        /// else red, in vanilla's own two colours.
        /// </summary>
        public sealed class AscensionPrereqLine
        {
            public readonly string Text;
            public readonly bool Met;

            public AscensionPrereqLine(string text, bool met)
            {
                Text = text;
                Met = met;
            }
        }

        /// <summary>
        /// The per-chain rows of the ascension gate, built from the same authored data as
        /// <see cref="AscensionLevelsMet"/> so the block and the gate cannot drift: chain name plus
        /// "current/gate" (2026-11-04, user chose 「链名后附当前等级与门槛」 over vanilla's bare name).
        ///
        /// Levels arrive through <paramref name="levelOf"/> exactly as in the gate — the game passes
        /// GameComponent_ResearchLoops.GetLevel, the verifier feeds whatever counter it wants — and
        /// names through <paramref name="prereqName"/>, which falls back to the raw defName so a typo
        /// in `prerequisite` shows up on the panel instead of naming nothing.
        ///
        /// Empty when there is nothing to gate on, which is the same condition that keeps the node
        /// locked: the panel then draws no block at all rather than a column of zeroes.
        /// </summary>
        public static List<AscensionPrereqLine> AscensionPrereqLines(DangerousTechInfo info,
            Func<string, int> levelOf, Func<string, string> prereqName)
        {
            var lines = new List<AscensionPrereqLine>();
            if (info == null || info.prereqLevel <= 0) return lines;
            if (info.prerequisites == null || info.prerequisites.Count == 0) return lines;
            if (levelOf == null) return lines;

            for (int i = 0; i < info.prerequisites.Count; i++)
            {
                string defName = info.prerequisites[i];
                int level = Math.Max(0, levelOf(defName));

                string shown = prereqName != null ? prereqName(defName) : null;
                if (string.IsNullOrEmpty(shown)) shown = defName;

                lines.Add(new AscensionPrereqLine(
                    shown + " " + level.ToString(Inv) + "/" + info.prereqLevel.ToString(Inv),
                    level >= info.prereqLevel));
            }
            return lines;
        }


        /// <summary>
        /// Node label: "name Lv.offered/maxLevel", where the name IS the effect (e.g. "远程伤害
        /// Lv.3/50"). Since 2026-10-23 there is exactly one authored name per loop — the old
        /// conceptName / conciseName pair and the settings toggle that switched between them are
        /// gone (user directive: 「默认名称改为直接效果，删除群星风格的那些命名」).
        /// Pure (no Verse) on purpose: the verifier compiles this file.
        /// </summary>
        public static string Label(ResearchLoopTemplate t, int completedLevels)
        {
            if (t == null) return null;
            int level = NextLevel(t, completedLevels);
            return Pick(t.name, t.nameEn) + " Lv." + level.ToString(Inv) + "/" + t.maxLevel.ToString(Inv);
        }

        /// <summary>
        /// Node description: the authored concept blurb, then the level status filled from
        /// descriptionFormula. The completion dialog shows this text, which at the moment of
        /// completion still describes the level that was just finished — consistent.
        ///
        /// <paramref name="bonusScale"/> scales the effect magnitudes the text quotes, so the
        /// description shows the number the buff actually applies when the player moved the
        /// effect slider off 1. <paramref name="dangerousZero"/> is the loop's dangerous tech
        /// having been researched: every quoted magnitude becomes 0.
        /// </summary>
        public static string Description(ResearchLoopTemplate t, int completedLevels,
            float bonusScale = 1f, bool dangerousZero = false)
        {
            if (t == null) return null;
            int level = NextLevel(t, completedLevels);

            string status;
            if (level >= t.maxLevel)
            {
                // Offering the last level: no "next level" marker at all (user: drop the 链尾 label
                // from the explanation). Keep the authored preamble and let a capstone note
                // (NextEffectText) stand on its own; an open-ended chain simply ends there —
                // the "研究完这一级即到顶" sentence was removed on user request.
                string formula = Pick(t.descriptionFormula, t.descriptionFormulaEn) ?? string.Empty;
                int tail = formula.IndexOf("{3}", StringComparison.Ordinal);
                string preamble = tail >= 0 ? formula.Substring(0, tail) : formula;
                status = Format(preamble + "{2}", level,
                    EffectMagnitude(t, level, bonusScale, dangerousZero), NextEffectText(t, level, bonusScale, dangerousZero),
                    NextLevelText(t, level));
            }
            else
            {
                status = Format(t.descriptionFormula, level,
                    EffectMagnitude(t, level, bonusScale, dangerousZero), NextEffectText(t, level, bonusScale, dangerousZero),
                    NextLevelText(t, level));
            }

            string blurb = Pick(t.description, t.descriptionEn);
            if (string.IsNullOrEmpty(blurb)) return status;
            return blurb + "\n\n" + status;        }

        /// <summary>
        /// The loop's per-level magnitude after the player's effect multiplier is applied.
        /// A non-positive or non-finite scale reads as 1 (authored values), and the product is
        /// sanitized again by LoopMath, so a bad setting degrades to "no change" rather than a
        /// NaN stat.
        ///
        /// This is the RATE form and only the rate form: it feeds 'add' and 'tiers' mode, where
        /// scaling the step linearly IS scaling the effect linearly. 'factor' mode must go
        /// through <see cref="EffectValue"/> instead — scaling a compounding rate is exponential
        /// and was the defect that drove 承伤 to zero.
        /// </summary>
        public static float EffectivePerLevel(ResearchLoopTemplate t, float bonusScale = 1f)
        {
            float authored = t != null && t.perLevel > 0f ? t.perLevel : 0.05f;
            float scale = LoopMath.Safe(bonusScale, 1f);
            if (scale <= 0f) scale = 1f;
            return LoopMath.Safe(authored * scale, authored);
        }

        /// <summary>
        /// THE number a loop is worth at a level: authored curve, player's effect multiplier and
        /// the authored clamp, in one place.
        ///
        /// Every consumer reads this — the buff hediff's stat modifiers, both Harmony patch
        /// chains, and the text in the research node and buff tooltip — so the number on the card
        /// is always the number the game applies, and a change to the scaling rule cannot reach
        /// one surface without reaching all of them.
        ///
        /// <paramref name="dangerousZero"/> is the one sanctioned way to drive a coefficient to
        /// literal zero: the loop's dangerous tech (see <see cref="DangerousTechInfo"/>) has been
        /// researched. It deliberately bypasses the lower-is-better floor — that floor exists to
        /// keep the settings SLIDER from creating immunity, not to protect against an authored
        /// endgame project — and it ignores the multiplier, because "×0 scaled by 5" is still 0.
        /// </summary>
        public static float EffectValue(ResearchLoopTemplate t, int level, float bonusScale = 1f, bool dangerousZero = false)
        {
            if (t == null) return 1f;
            if (dangerousZero) return 0f;
            if (level <= 0) return t.mode == LoopMath.ModeAdd || t.mode == LoopMath.ModeTiers ? 0f : 1f;

            float authored = t.perLevel > 0f ? t.perLevel : 0.05f;
            float scale = LoopMath.Safe(bonusScale, 1f);
            if (scale <= 0f) scale = 1f;
            // Item-quality loop: the benefit is a fixed tier count, so the effect multiplier must
            // not scale it — it only changes how cheap the research was (see LevelCost).
            if (t.costInverseEffectScale) scale = 1f;

            if (t.mode == LoopMath.ModeAdd)
            {
                return LoopMath.Clamp(LoopMath.Flat(level, EffectivePerLevel(t, scale)),
                    t.clampMin, t.clampMax);
            }

            if (t.mode == LoopMath.ModeTiers)
            {
                return LoopMath.QualityTiers(level, EffectivePerLevel(t, scale));
            }

            // 'factor': linear on the distance from 1, floored so a lower-is-better stat can never
            // be driven to zero (see LoopMath.ScaledFactor).
            return LoopMath.ScaledFactor(level, authored, t.lowerIsBetter, scale);
        }

        /// <summary>
        /// {1} for a level: the effect magnitude in the loop's own units, substituted into the
        /// authored effectText (e.g. "殖民者采矿速度 ×1.41").
        /// </summary>
        public static string EffectMagnitude(ResearchLoopTemplate t, int level, float bonusScale = 1f, bool dangerousZero = false)
        {
            if (t == null || level <= 0) return string.Empty;
            string magnitude;
            if (t.mode == LoopMath.ModeAdd)
            {
                magnitude = "+" + EffectValue(t, level, bonusScale, dangerousZero).ToString("0.##", Inv);
            }
            else if (t.mode == LoopMath.ModeTiers)
            {
                magnitude = EffectValue(t, level, bonusScale, dangerousZero).ToString("0.##", Inv);
            }
            else
            {
                // 2 decimals reads better in an info card; the stat tooltip keeps the long form.
                magnitude = EffectValue(t, level, bonusScale, dangerousZero).ToString("0.##", Inv);
            }
            return Format(Pick(t.effectText, t.effectTextEn), level, magnitude, string.Empty, string.Empty);
        }

        /// <summary>
        /// {2} for a level: the next level's effect. On the final level this is the authored
        /// capstone note (capped chains) or empty — open-ended chains end after the preamble.
        /// </summary>
        public static string NextEffectText(ResearchLoopTemplate t, int level, float bonusScale = 1f, bool dangerousZero = false)
        {
            if (t == null || level <= 0) return string.Empty;
            if (level < t.maxLevel) return EffectMagnitude(t, level + 1, bonusScale, dangerousZero);
            string note = Pick(t.capstoneNote, t.capstoneNoteEn);
            if (t.capstone && !string.IsNullOrEmpty(note)) return note;
            // Open-ended chains: nothing after the preamble. The end-of-chain sentence was removed
            // on user request — the last level simply stops after "当前为第 N 级：…。".
            return string.Empty;
        }

        /// <summary>
        /// {3} for a level: the next-level phrase ("下一级（第 N 级）") on every level that still has
        /// one. On the final level it is empty, so the description never prints a "链尾" marker
        /// (user directive: the chain tail must not be labelled in the explanation); a capstone
        /// note, when one exists, is carried by <see cref="NextEffectText"/> instead.
        /// </summary>
        public static string NextLevelText(ResearchLoopTemplate t, int level)
        {
            if (t == null || level <= 0) return string.Empty;
            if (level < t.maxLevel)
            {
                return English
                    ? "next level (level " + (level + 1).ToString(Inv) + ")"
                    : "下一级（第 " + (level + 1).ToString(Inv) + " 级）";
            }
            return string.Empty;
        }

        /// <summary>
        /// Substitutes {0}=level, {1}=this level's effect, {2}=next level's effect,
        /// {3}=next-level phrase. Unknown placeholders are left untouched so the runtime
        /// self-check (which rejects descriptions still containing '{') catches them.
        /// </summary>
        public static string Format(string formula, int level, string effect,
            string nextEffect, string nextLevelText)
        {
            if (formula == null) return string.Empty;
            return formula
                .Replace("{1}", effect ?? string.Empty)
                .Replace("{2}", nextEffect ?? string.Empty)
                .Replace("{3}", nextLevelText ?? string.Empty)
                .Replace("{0}", level.ToString(Inv));
        }
    }
}
