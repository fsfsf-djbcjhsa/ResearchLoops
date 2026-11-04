using System.Collections.Generic;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Which loops currently have 自动循环 (auto-loop) switched on, persisted per save.
    ///
    /// A loop is "auto-looping" once the player toggles its 自动循环 button in the research panel.
    /// While on, every level completion of that loop re-arms the same ResearchProjectDef and shows a
    /// small corner toast instead of the vanilla completion dialog; the chain's final level
    /// additionally raises a fullscreen prompt and then switches the flag off.
    ///
    /// This is deliberately a SEPARATE GameComponent from GameComponent_ResearchLoops: the auto-loop
    /// feature must not touch that component's logic, and its state only ever carries loop defNames,
    /// so it is self-contained. RimWorld auto-instantiates it via Game.FillComponents(), so no manual
    /// registration in Mod.cs is needed.
    /// </summary>
    public class AutoLoopState : GameComponent
    {
        private HashSet<string> loops = new HashSet<string>();

        private static Game cachedGame;
        private static AutoLoopState cachedComponent;

        public AutoLoopState(Game game)
        {
        }

        /// <summary>O(1) accessor mirroring GameComponent_ResearchLoops.Current; null before a game exists.</summary>
        public static AutoLoopState Current
        {
            get
            {
                Game game = Verse.Current.Game;
                if (game == null)
                {
                    cachedGame = null;
                    cachedComponent = null;
                    return null;
                }
                if (game != cachedGame)
                {
                    cachedGame = game;
                    cachedComponent = game.GetComponent<AutoLoopState>();
                }
                return cachedComponent;
            }
        }

        public static bool IsEnabled(string loopName)
        {
            if (string.IsNullOrEmpty(loopName)) return false;
            AutoLoopState comp = Current;
            return comp != null && comp.loops.Contains(loopName);
        }

        public static void SetEnabled(string loopName, bool value)
        {
            if (string.IsNullOrEmpty(loopName)) return;
            AutoLoopState comp = Current;
            if (comp == null) return;
            if (value) comp.loops.Add(loopName);
            else comp.loops.Remove(loopName);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref loops, "autoLoops", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (loops == null) loops = new HashSet<string>();
                // Drop flags for loops that no longer exist. Chain-complete pruning is deliberately
                // NOT done here: counts may not be fully re-projected at this point, and the flag is
                // harmless until re-checked at use time (the button hides a finished chain; the
                // completion path clears the flag on the final level).
                if (ResearchLoopTemplates.All.Count > 0)
                {
                    loops.RemoveWhere(loop => ResearchLoopTemplates.Get(loop) == null);
                }
            }
        }
    }
}
