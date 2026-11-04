using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Attached to every generated loop def. Carries only the identity: which loop this def
    /// belongs to. The level itself is NOT stored here — it lives in
    /// GameComponent_ResearchLoops as a string-keyed count and is projected onto the def by
    /// ResearchLoopProgress (baseCost/label/description). The predecessor carried
    /// level/effectText/nextEffectText here for a def-per-level design that never generated.
    /// </summary>
    public class ResearchLoopExtension : DefModExtension
    {
        /// <summary>Template defName, e.g. "RL_Mining". The join key for counter and StatParts.</summary>
        public string loop;

        public bool IsLoopDef
        {
            get { return !string.IsNullOrEmpty(loop); }
        }
    }
}
