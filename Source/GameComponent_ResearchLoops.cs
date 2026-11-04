using System.Collections.Generic;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The single source of truth for how many times each research loop has been completed.
    /// Auto-instantiated by RimWorld because it is a GameComponent; persisted via IExposable.
    ///
    /// Keyed by loop defName STRING, not by def: the counter must survive def lookups that
    /// are not possible at patch time, and the old def-keyed dictionary needed a
    /// DefDatabase round-trip on every stat query. defNames are stable authored ids.
    ///
    /// Hot path: the buff hediff's dynamic stage caches per version, so GetLevel is cheap on
    /// every stat evaluation; the version counter below is what invalidates those caches.
    /// <see cref="Current"/> caches the component and only re-resolves when the Game instance
    /// changes (new game / load).
    ///
    /// This component also owns restoring def state: the loop defs' baseCost/label/description
    /// reflect the counter, and defs are static per process, so they are re-projected here on
    /// game load and on new game (otherwise a loop def levelled up in one save would keep that
    /// level in the next save).
    /// </summary>
    public class GameComponent_ResearchLoops : GameComponent
    {
        private Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>
        /// Per-skill bonus levels, pre-summed from <see cref="counts"/> and the template data.
        /// Patch_SkillLevel runs inside SkillRecord.GetLevel, which every stat evaluation touches,
        /// so the sum must not be recomputed per call: it is built once and dropped whenever a
        /// level is completed or the game state is (re)loaded.
        /// </summary>
        private Dictionary<string, int> skillBonus;

        private static Game cachedGame;
        private static GameComponent_ResearchLoops cachedComponent;

        /// <summary>
        /// Bumped on every level completion and on every game (re)load. The buff hediff's dynamic
        /// stage caches against this number, so a completed level invalidates every pawn's buff
        /// without touching the pawns themselves (DESIGN §15).
        /// </summary>
        public int BuffVersion { get; private set; }

        /// <summary>
        /// Per-pawn/skill base passion snapshot for the passion loop (key = "pawnID:skillDefName",
        /// value = raw RimWorld.Passion ordinal, which under VSE is the PassionDef.index). Captured
        /// the first time a pawn is affected and restored on revert, so the passion bonus is fully
        /// reversible and survives load. See <see cref="ResearchLoopPassion"/>.
        /// </summary>
        private Dictionary<string, int> passionBase;

        /// <summary>BuffVersion of the running game, 0 before a game exists.</summary>
        public static int CurrentBuffVersion
        {
            get
            {
                GameComponent_ResearchLoops comp = Current;
                return comp == null ? 0 : comp.BuffVersion;
            }
        }

        public GameComponent_ResearchLoops(Game game)
        {
        }

        /// <summary>
        /// O(1) accessor. Returns null before a game exists, which every caller must tolerate.
        /// Note: <c>Verse.Current</c> must be fully qualified — GameComponent defines its own
        /// instance member named Current, which would otherwise shadow the static class.
        /// </summary>
        public static GameComponent_ResearchLoops Current
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
                    cachedComponent = game.GetComponent<GameComponent_ResearchLoops>();
                }
                return cachedComponent;
            }
        }

        /// <summary>
        /// Completed levels for a loop, keyed by template defName (e.g. "RL_Mining").
        /// Returns 0 when unknown, so a missing entry and an un-researched loop are the same state.
        /// </summary>
        public static int GetLevel(string loopName)
        {
            if (string.IsNullOrEmpty(loopName)) return 0;
            GameComponent_ResearchLoops comp = Current;
            if (comp == null) return 0;
            return comp.GetCount(loopName);
        }

        /// <summary>
        /// Raw count lookup, sanitized on read: a counter that is negative or absurd (corrupted
        /// save, or one written by a future/older build) is clamped instead of being fed to the
        /// loop math, where NaN or a runaway multiplier could poison every stat on the pawn.
        /// Returns 0 when absent.
        /// </summary>
        public int GetCount(string loopName)
        {
            if (string.IsNullOrEmpty(loopName)) return 0;
            int value;
            if (counts.TryGetValue(loopName, out value)) return LoopMath.SanitizeLevel(value);
            return 0;
        }

        /// <summary>Records one more completion of the named loop.</summary>
        public void Increment(string loopName)
        {
            if (string.IsNullOrEmpty(loopName)) return;
            counts[loopName] = LoopMath.SanitizeLevel(GetCount(loopName) + 1);

            // Drop the per-skill cache, then mirror the new totals into the gene's aptitudes and
            // the buff hediffs so both panels show the change immediately rather than at the next
            // tick.
            NotifySettingsChanged();
        }

        /// <summary>
        /// Bonus skill levels this colony has earned for one vanilla skill, or 0. O(1) after the
        /// first call per level-up; safe to call before a game exists.
        /// </summary>
        public static int GetSkillBonus(string skillDefName)
        {
            if (string.IsNullOrEmpty(skillDefName)) return 0;
            GameComponent_ResearchLoops comp = Current;
            return comp == null ? 0 : comp.SkillBonusFor(skillDefName);
        }

        /// <summary>
        /// The skill bonus for one vanilla skill, scaled by the effect multiplier and rounded up. The
        /// cached per-skill map stores the RAW completed levels; the multiplier is applied here so a
        /// single line (this method) governs both the gene card and the live bonus. User directive:
        /// the skill/gene numbers follow the 效果倍率 slider, with ceiling rounding so a low
        /// multiplier keeps at least +1 instead of erasing the bonus.
        /// </summary>
        public int SkillBonusFor(string skillDefName)
        {
            if (string.IsNullOrEmpty(skillDefName)) return 0;
            if (skillBonus == null) RebuildSkillBonus();

            int value;
            if (!skillBonus.TryGetValue(skillDefName, out value)) return 0;
            return LoopSkillPlan.ScaledBonus(value, ResearchLoopsConfig.BonusScale);
        }

        private void RebuildSkillBonus()
        {
            skillBonus = new Dictionary<string, int>(System.StringComparer.Ordinal);
            foreach (string skill in LoopSkillPlan.Skills(ResearchLoopTemplates.All))
            {
                int bonus = LoopSkillPlan.Bonus(ResearchLoopTemplates.All, GetCount, skill);
                if (bonus > 0) skillBonus[skill] = bonus;
            }
        }

        // ─── passion-loop base snapshot (ResearchLoopPassion) ──────────────────────────────

        public bool TryGetPassionBase(string key, out int value)
        {
            value = 0;
            if (passionBase == null) return false;
            return passionBase.TryGetValue(key, out value);
        }

        public void SetPassionBase(string key, int value)
        {
            if (passionBase == null) passionBase = new Dictionary<string, int>(System.StringComparer.Ordinal);
            passionBase[key] = value;
        }

        public void ClearPassionBase(string key)
        {
            if (passionBase != null) passionBase.Remove(key);
        }

        /// <summary>
        /// Re-projects every def, gene aptitude and buff from the current counters and settings.
        /// One entry point for every state change: a completed level, a game (re)load, a new game
        /// and a settings/preset change all end here, so they cannot drift apart.
        ///
        /// The gene pass is part of this on purpose (user directive: 「基因要记得刷新」): the gene
        /// carries the skill bonuses that the skill panel renders, so a state change that only
        /// updated the hediff would leave the gene card showing the previous level until the next
        /// 250-tick sweep. EnsureAll covers pawns that joined since the last sweep, SyncAptitudes
        /// rewrites the values, and SyncAptitudes dirties every pawn's cached Aptitude when it
        /// actually changes something.
        /// </summary>
        public void NotifySettingsChanged()
        {
            skillBonus = null;
            ResearchLoopProgress.ApplyAll(this);
            ResearchLoopGene.EnsureAll();
            ResearchLoopGene.SyncAptitudes(this);
            BuffVersion++;
            ResearchLoopBuff.SyncAll();
            AscensionBuff.SyncAll();
            ResearchLoopPassion.ApplyAll();
        }

        /// <summary>
        /// Sanitizes a just-loaded counter dictionary in place. Scribe gives back whatever the
        /// file held — including negative or absurd values from a corrupted or hand-edited save —
        /// and every one of those would be a stat multiplier one level off the rails. The repair
        /// is logged once per bad key, so the cause is visible in Player.log without spamming
        /// one line per call site.
        /// </summary>
        private void SanitizeCounts()
        {
            List<string> badKeys = null;
            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Key == null || entry.Value < 0 || entry.Value > LoopMath.LevelCeiling)
                {
                    if (badKeys == null) badKeys = new List<string>();
                    badKeys.Add(entry.Key);
                }
            }
            if (badKeys == null) return;

            for (int i = 0; i < badKeys.Count; i++)
            {
                string key = badKeys[i];
                if (key == null)
                {
                    counts.Remove(null);
                    Log.Warning("[ResearchLoops] dropped a counter entry with a null key");
                    continue;
                }
                int value = counts[key];
                counts[key] = LoopMath.SanitizeLevel(value);
                Log.Warning("[ResearchLoops] counter " + key + " held an out-of-range value (" +
                            value + ") after load; clamped to " + counts[key]);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref counts, "counts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref passionBase, "passionBase", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (counts == null)
                {
                    counts = new Dictionary<string, int>();
                }
                else
                {
                    SanitizeCounts();
                }
                if (passionBase == null) passionBase = new Dictionary<string, int>();
                NotifySettingsChanged();
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            NotifySettingsChanged();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            NotifySettingsChanged();
        }

        /// <summary>
        /// Keeps the 「科技循环」 gene on pawns that joined after the gene pass: new recruits, slaves
        /// bought later, pawns that arrive by quest — and keeps the loop buff in step with
        /// eligibility (a pawn taken prisoner must lose it). Once every 250 ticks is plenty for
        /// both, and each Ensure() is a no-op when the pawn already carries its gear.
        /// </summary>
        public override void GameComponentTick()
        {
            if (Find.TickManager == null || Find.TickManager.TicksGame % 250 != 0) return;
            ResearchLoopGene.EnsureAll();
            ResearchLoopBuff.SyncAll();
            AscensionBuff.SyncAll();
            SyntheticAscension.SyncAll();
            ResearchLoopPassion.ApplyAll();
        }
    }
}
