using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The mood gift of the ascensions (2026-10-25 user directive: 「每完成一个飞升加15点心情，
    /// 描述就是群星里的那四句」). Each researched ascension grants +15 mood, shown as its own
    /// 「飞升之悦 · &lt;ascension&gt;」 thought whose description is that ascension's Stellaris flavour line.
    /// Only the COMPLETED ascensions' thoughts are active, so the mood panel lists exactly the lines
    /// of what the colony has actually achieved (user chose "only show the finished ones").
    ///
    /// One worker serves all four ThoughtDefs: the def it is attached to (def.defName) selects which
    /// ascension effect to test, so a fifth ascension is one switch arm plus one XML def.
    /// </summary>
    public class ThoughtWorker_AscensionJoy : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !LoopEligibility.IsSupported(p)) return ThoughtState.Inactive;

            string effect = EffectFor(def.defName);
            if (effect == null) return ThoughtState.Inactive;

            return DangerousTech.EffectResearched(effect)
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.Inactive;
        }

        private static string EffectFor(string defName)
        {
            switch (defName)
            {
                case "RL_AscensionJoy_Mind": return DangerousTech.EffectAscension;
                case "RL_AscensionJoy_Mech": return DangerousTech.EffectSynthetic;
                case "RL_AscensionJoy_Body": return DangerousTech.EffectCybernetic;
                case "RL_AscensionJoy_Gene": return DangerousTech.EffectGene;
                default: return null;
            }
        }
    }
}
