using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Auto-loop research-flow patches — merged into Source/ and compiled into ResearchLoops.dll.
    ///
    /// Prefix: when the just-finished project is a loop with auto-loop ON, suppress vanilla's
    /// per-level completion dialog + letter — the feature replaces them with a corner toast (mid-chain)
    /// or a fullscreen prompt (final level). The finished level number is captured HERE, from the
    /// pre-increment counter, so it is exact regardless of which FinishProject postfix runs first.
    ///
    /// Postfix: after vanilla has nulled ResearchManager.currentProj at the end of FinishProject,
    /// re-arm the same def so the next level starts automatically; then surface the toast or the
    /// fullscreen prompt.
    ///
    /// Re-entrancy: FinishProject recurses into its prerequisites (never loops, but the nested call
    /// would otherwise clobber the outer call's stashed state). A depth guard makes the postfix fire
    /// exactly once, for the outermost call.
    ///
    /// Mod.cs's PatchAll() picks up both [HarmonyPatch] classes automatically. See AutoLoop/README.md.
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class Patch_AutoLoopFinishProject
    {
        private static int nestDepth;
        private static bool armed;
        private static ResearchProjectDef armedDef;
        private static int armedLevel;
        private static bool armedChainComplete;

        // ref args let the prefix rewrite the dialog/letter flags the original would have raised.
        public static void Prefix(ResearchProjectDef proj, ref bool doCompletionDialog, ref bool doCompletionLetter)
        {
            nestDepth++;
            try
            {
                if (nestDepth > 1) return; // nested prerequisite call: leave the outer call's state alone
                armed = false;
                ResearchLoopTemplate template = ResearchLoopProgress.TemplateOf(proj);
                if (template == null) return;
                if (!AutoLoopState.IsEnabled(template.defName)) return;

                // Replace vanilla's per-level dialog + letter with our own notifications.
                doCompletionDialog = false;
                doCompletionLetter = false;

                // Counter has not been incremented yet (this is a prefix): the level finishing right
                // now is count+1.
                int before = GameComponent_ResearchLoops.GetLevel(template.defName);
                armedLevel = before + 1;
                armedDef = proj;
                armedChainComplete = armedLevel >= template.maxLevel;
                armed = true;
            }
            catch (System.Exception ex)
            {
                Log.Error("[ResearchLoops] AutoLoop FinishProject prefix failed: " + ex);
            }
        }

        public static void Postfix()
        {
            int remaining = nestDepth - 1;
            nestDepth = remaining;
            if (remaining > 0) return;   // unwinding a nested prerequisite call
            if (!armed) return;
            armed = false;
            try
            {
                ResearchLoopTemplate template = ResearchLoopProgress.TemplateOf(armedDef);
                if (template == null) return;

                if (armedChainComplete)
                {
                    // Whole chain done: fullscreen prompt, then drop the flag (nothing left to loop).
                    AutoLoopState.SetEnabled(template.defName, false);
                    AutoLoopUI.ShowChainComplete(template, armedLevel);
                    return;
                }

                // Vanilla nulled currentProj at the very end of FinishProject (confirmed in
                // Assembly-CSharp). Re-arm the same def so the next level — still the same
                // ResearchProjectDef — begins without player input.
                Find.ResearchManager.SetCurrentProject(armedDef);
                AutoLoopUI.ShowLevelToast(template, armedLevel);
            }
            catch (System.Exception ex)
            {
                Log.Error("[ResearchLoops] AutoLoop FinishProject postfix failed: " + ex);
            }
        }
    }

    /// <summary>
    /// Adds the 自动循环 toggle button to the research panel, just above the vanilla
    /// "开始研究 / 停止研究" button. Only loop defs get the button; a finished chain hides it.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), "DrawStartButton")]
    public static class Patch_AutoLoopDrawStartButton
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_Research, ResearchProjectDef> SelectedProject =
            AccessTools.FieldRefAccess<MainTabWindow_Research, ResearchProjectDef>("selectedProject");

        public static void Postfix(Rect startButRect, MainTabWindow_Research __instance)
        {
            try
            {
                AutoLoopUI.DrawAutoLoopButton(startButRect, SelectedProject(__instance));
            }
            catch (System.Exception ex)
            {
                Log.Error("[ResearchLoops] AutoLoop DrawStartButton postfix failed: " + ex);
            }
        }
    }
}
