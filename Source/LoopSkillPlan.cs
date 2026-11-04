using System;
using System.Collections.Generic;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Pure rules for "which skill does a loop level up, and how much is it worth right now".
    ///
    /// Deliberately Verse-free so the verification project can compile this file and assert the
    /// arithmetic without loading Assembly-CSharp: the runtime only supplies the level lookup
    /// (GameComponent_ResearchLoops) and the SkillDef name.
    ///
    /// Several loops may feed the SAME skill on purpose (采掘 + 深钻 → Mining, 冶炼 + 品质 +
    /// 机械维修 → Crafting), because vanilla only has 12 skills and the work types collapse onto
    /// them. The bonus is therefore a SUM, not a max: two finished chains on one skill really do
    /// give twice the points.
    /// </summary>
    public static class LoopSkillPlan
    {
        /// <summary>delivery value that marks a skill loop (one point per completed level).</summary>
        public const string DeliverySkill = "skill";

        /// <summary>
        /// Bonus levels for one skill: the sum of the completed levels of every loop whose
        /// <c>skill</c> matches. Unknown loops and level 0 contribute nothing.
        /// </summary>
        public static int Bonus(IEnumerable<ResearchLoopTemplate> templates, Func<string, int> levelOf,
            string skillDefName)
        {
            if (templates == null || levelOf == null || string.IsNullOrEmpty(skillDefName)) return 0;

            int total = 0;
            foreach (ResearchLoopTemplate template in templates)
            {
                if (template == null || !template.FeedsSkill(skillDefName)) continue;

                int level = levelOf(template.defName);
                if (level > 0) total += level;
            }
            return total;
        }

        /// <summary>
        /// Every skill the authored loops feed, in first-seen order. Used by the runtime cache so it
        /// only ever queries the skills this mod actually touches.
        /// </summary>
        public static List<string> Skills(IEnumerable<ResearchLoopTemplate> templates)
        {
            var result = new List<string>();
            if (templates == null) return result;

            foreach (ResearchLoopTemplate template in templates)
            {
                if (template == null || template.skills == null) continue;

                for (int i = 0; i < template.skills.Count; i++)
                {
                    string skill = template.skills[i];
                    if (!string.IsNullOrEmpty(skill) && !result.Contains(skill)) result.Add(skill);
                }
            }
            return result;
        }

        /// <summary>
        /// Scales a raw skill bonus by the effect multiplier and rounds UP (ceiling), so a low
        /// multiplier (e.g. ×0.1) never drops a positive bonus to 0 — at least +1 stays. This is the
        /// single source of truth for "how many skill levels the loops grant", shared by both the
        /// gene card (written via SyncAptitudes) and the live bonus (ApplySkillBonus); keeping them
        /// in one place means the panel and the actual effect can never disagree.
        ///
        /// User directive: the skill/gene numbers follow the 效果倍率 slider. ×1 leaves the raw level
        /// untouched, ×2 doubles it, ×0.5 halves it, ×0.1 floors it at +1 instead of erasing it.
        /// </summary>
        public static int ScaledBonus(int raw, float scale)
        {
            if (raw <= 0) return 0;

            float s = scale;
            if (s <= 0f) s = 1f;   // a non-positive multiplier is meaningless; treat as ×1

            double scaled = System.Math.Ceiling(raw * (double)s);
            int result = (int)scaled;
            if (result < 1) result = 1;   // ceiling guarantees >=1 for positive raw, but guard anyway
            return result;
        }
    }
}
