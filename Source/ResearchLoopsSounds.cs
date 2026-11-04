using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Custom sounds, ported from Stellaris stings: two research stings (start / complete) and
    /// one sting per ascension. Audio lives in <c>Sounds/</c> (ogg/Vorbis), defs in
    /// <c>Defs/SoundDefs/ResearchLoopsSounds.xml</c>. All are played on the camera as UI
    /// notifications; every lookup fails silent so a missing clip can never break research.
    /// </summary>
    public static class ResearchLoopsSounds
    {
        // Research feedback (fires per loop level).
        public const string ResearchStart = "RL_ResearchStart";
        public const string ResearchComplete = "RL_ResearchComplete";

        // Fires once when a whole loop chain is finished (every level done) — grander than the
        // per-level ResearchComplete sting.
        public const string ChainComplete = "RL_ChainComplete";

        // One sting per ascension, keyed by the top-level &lt;Dangerous&gt; defName that the
        // player finishes to complete that ascension.
        private static readonly Dictionary<string, string> AscensionSoundByDef =
            new Dictionary<string, string>
            {
                { "RL_MindOverMatter",     "RL_AscensionMindOverMatter" }, // 心胜于物  (psionic)
                { "RL_SyntheticEvolution", "RL_AscensionSynthetic"      }, // 合成进化  (synthetic)
                { "RL_CyberneticAscension","RL_AscensionCybernetic"     }, // 血肉苦弱  (cybernetic)
                { "RL_GeneAscension",      "RL_AscensionGenetic"        }, // 设计进化  (genetic)
            };

        /// <summary>Plays a ResearchLoops SoundDef on the camera; silent if the def is missing.</summary>
        public static void Play(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return;

            // Fresh lookup, never cached: a language switch clears and rebuilds every
            // DefDatabase, so a cached SoundDef would be an orphaned object whose clip is
            // already unloaded — the sting would silently stop playing after a switch.
            SoundDef def = DefDatabase<SoundDef>.GetNamedSilentFail(defName);
            if (def == null) return;

            SoundInfo info = SoundInfo.OnCamera();
            info.volumeFactor = ResearchLoopsConfig.SoundVolume;
            def.PlayOneShot(info);
        }

        public static void PlayResearchStart() => Play(ResearchStart);

        public static void PlayResearchComplete() => Play(ResearchComplete);

        public static void PlayChainComplete() => Play(ChainComplete);

        /// <summary>
        /// Plays the ascension sting when the finished dangerous def is one of the four ascensions.
        /// Nested zeroing techs (which also reach <see cref="DangerousTech.IsDangerousDef"/>) are
        /// not in the map, so they stay silent.
        /// </summary>
        public static void PlayAscensionIfAny(ResearchProjectDef proj)
        {
            if (proj == null) return;

            string sound;
            if (AscensionSoundByDef.TryGetValue(proj.defName, out sound))
                Play(sound);
        }
    }

    /// <summary>
    /// Research-start sting: fires the moment a loop research begins — both when the player clicks
    /// 开始研究 and when AutoLoop re-arms the next level (that path also goes through
    /// SetCurrentProject). Non-loop projects and a null project (stopping research) are ignored.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.SetCurrentProject))]
    public static class Patch_SetCurrentProject
    {
        public static void Postfix(ResearchProjectDef proj)
        {
            if (proj == null) return;
            if (!ResearchLoopProgress.IsLoop(proj)) return;

            ResearchLoopsSounds.PlayResearchStart();
        }
    }
}
