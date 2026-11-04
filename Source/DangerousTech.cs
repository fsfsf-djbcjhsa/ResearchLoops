using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// The dangerous techs (user directive, 2026-10-22): one-shot research projects that appear
    /// once their parent chain is complete and drive that chain's coefficient to literal zero —
    /// the nested form was 告别休息 / 告别饮食 / 告别娱乐. Authored per loop as a &lt;dangerous&gt;
    /// element in Data/ResearchLoopTemplates.xml and generated as real ResearchProjectDefs by
    /// PatchOperation_GenerateLoopDefs (defName = parent + <see cref="ResearchLoopsIds.DangerousSuffix"/>).
    ///
    /// 2026-10-23 (user directive): ALL SIX nested &lt;dangerous&gt; elements were deleted, so the
    /// per-loop form below is now dormant code — no template authors one, and the parser and
    /// generator keep the branch only so re-adding it needs no new code. The live form is the
    /// STANDALONE capstone (see below), which is also the only remaining implementation of a
    /// coefficient zero (via its authored `zeroes` list, consumed by
    /// <see cref="IsZeroedByAscension"/>).
    ///
    /// "TAKES OVER THE PARENT'S POSITION" (user directive): vanilla reports two defs sharing a
    /// researchView coordinate in the same tab as a config error, so the handover is done at
    /// runtime instead of by authoring. While the chain is incomplete the parent holds the
    /// authored coordinate and the dangerous def is parked just above it; the moment the chain
    /// reads as finished the two SWAP — the dangerous node lands exactly where the chain node
    /// was and the parent steps aside. The parked def is never drawn (see
    /// <see cref="Patch_VisibleResearchProjects"/>), and because vanilla sizes the scroll view
    /// from the visible projects with Mathf.Max only, a small parked coordinate cannot stretch
    /// the page.
    ///
    /// Other surfaces:
        ///   - COLOURED TITLE: <see cref="Patch_GetLabel"/> wraps a dangerous node title in a
        ///     rich-text colour tag. The four standalone ascensions each get a signature colour
        ///     (keyed by `effect`, see <see cref="AscensionColors"/>); nested dangerous techs keep
        ///     the warning red. Unity IMGUI parses the tag; Text.CalcHeight strips tags, so the
        ///     node layout is unaffected.
    ///   - THE ZERO: ResearchLoopPlan.EffectValue(..., dangerousZero: true) returns 0, so the buff
    ///     stat factors, the patch chains, the research node text and the buff tooltip all apply —
    ///     and quote — the same number.
    ///
    /// Completion state is vanilla's own (IsFinished on the dangerous def), so it survives
    /// save/load with no data of this mod's own; Patch_FinishProject only triggers a re-projection
    /// so the parent chain's text and buff switch to ×0 the moment it is researched.
    ///
    /// STANDALONE DANGEROUS TECHS (2026-10-22, 心胜于物): a &lt;Dangerous&gt; top-level element is a
    /// capstone of SEVERAL chains. It shares everything above except the handover — it has no
    /// parent node to replace, so it keeps its authored coordinate (at the HEAD of the row its chains
    /// occupy, 2026-11-04 user 「飞升要位于最前边」) and its effect is the authored `effect` kind
    /// rather than "zero the parent". It is drawn on the page from the first game (2026-11-04 user
    /// directive) and greyed until every chain in its authored `prerequisite` list has reached
    /// `prereqLevel` — this mod's own level counters, never vanilla's "is that chain finished" test
    /// (<see cref="PrereqLevelsMet"/>). That list is deliberately NOT written onto the def: vanilla
    /// reads it as a rule (chain 练满 50 级) and colours its own "Prerequisites:" block the same way,
    /// and correcting either is a UI redraw, which the user ruled out (2026-11-04 「不要动 UI」). The
    /// node's description carries the requirement, and <see cref="Patch_PrerequisitesCompleted"/>
    /// feeds the level check to vanilla's lock reason so a greyed node is never a node without a
    /// reason. <see cref="Ascended"/> is that kind's state.
    /// </summary>
    public static class DangerousTech
    {
        /// <summary>Node title colour for nested dangerous techs: a warning red. No nested tech is
        /// authored since 2026-10-23, so this currently only guards the retained branch.</summary>
        public static readonly Color LabelColor = new Color(1f, 0.3f, 0.3f);

        /// <summary>
        /// Signature colour of each standalone ascension, keyed by its authored `effect` kind. The
        /// four capstones read at a glance in the research tree instead of all sharing the warning
        /// red; nested dangerous techs keep <see cref="LabelColor"/>.
        /// </summary>
        private static readonly Dictionary<string, Color> AscensionColors =
            new Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase)
        {
            { EffectAscension, new Color(0.70f, 0.40f, 1.00f) },   // 心胜于物 — psychic violet
            { EffectSynthetic, new Color(0.20f, 0.80f, 0.90f) },   // 合成进化 — mech cyan
            { EffectCybernetic, new Color(1.00f, 0.60f, 0.10f) },  // 血肉苦弱 — body orange
            { EffectGene, new Color(0.40f, 0.90f, 0.40f) },         // 设计进化 — gene green
        };

        /// <summary>Node title colour for a dangerous tech: ascensions get their signature colour,
        /// nested dangerous techs keep the warning red.</summary>
        public static Color ColorFor(DangerousTechInfo info)
        {
            if (info != null && !string.IsNullOrEmpty(info.effect))
            {
                Color c;
                if (AscensionColors.TryGetValue(info.effect, out c)) return c;
            }
            return LabelColor;
        }

        /// <summary>
        /// Where a dangerous def (before the handover) or a completed parent chain (after it) is
        /// parked: just above the parent's own coordinate. Derived, never authored, so the three
        /// dangerous techs cannot share a spot (their parents' coordinates are unique — vanilla
        /// reports two defs on one coordinate as a config error) and the spot can never land on a
        /// grid coordinate (the page grid steps 1.2 in Y from 1; 0.1 below a node is always free).
        /// Negative coordinates are a vanilla config error, and ViewSize takes Mathf.Max of the
        /// VISIBLE projects only, so a small parked coordinate neither errors nor stretches the
        /// page — and the filter below keeps whatever sits here out of the drawn list entirely.
        /// </summary>
        public const float ParkOffsetY = ResearchLoopsIds.DangerousParkOffsetY;

        /// <summary>The parking spot of one dangerous tech: just above its parent's node.</summary>
        public static float ParkX(ResearchLoopTemplate parent)
        {
            return parent == null ? 0f : parent.researchViewX;
        }

        public static float ParkY(ResearchLoopTemplate parent)
        {
            if (parent == null) return 0f;
            float y = parent.researchViewY - ParkOffsetY;
            return y < 0.05f ? 0.05f : y;
        }

        /// <summary>dangerous defName -> parent loop template. Built once from the authored data.</summary>
        private static Dictionary<string, ResearchLoopTemplate> parents;

        /// <summary>standalone dangerous defName -> authored info. Built once, may stay empty.</summary>
        private static Dictionary<string, DangerousTechInfo> standalone;

        /// <summary>The dangerous-tech defName of a parent loop (e.g. "RL_RestFall_Danger").</summary>
        public static string DefNameFor(ResearchLoopTemplate parent)
        {
            return parent == null ? null : parent.defName + ResearchLoopsIds.DangerousSuffix;
        }

        private static void EnsureParents()
        {
            if (parents != null) return;
            parents = new Dictionary<string, ResearchLoopTemplate>(System.StringComparer.Ordinal);
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.dangerous == null) continue;
                parents[DefNameFor(template)] = template;
            }
        }

        private static void EnsureStandalone()
        {
            if (standalone != null) return;
            standalone = new Dictionary<string, DangerousTechInfo>(System.StringComparer.Ordinal);
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                if (info == null || string.IsNullOrEmpty(info.defName)) continue;
                standalone[info.defName] = info;
            }
        }

        /// <summary>The parent loop template of a dangerous def, or null for any other def.</summary>
        public static ResearchLoopTemplate ParentOf(ResearchProjectDef def)
        {
            if (def == null) return null;
            EnsureParents();
            ResearchLoopTemplate parent;
            return parents.TryGetValue(def.defName, out parent) ? parent : null;
        }

        /// <summary>
        /// The authored entry behind any dangerous def — nested (parent's &lt;dangerous&gt;) or
        /// standalone (top-level &lt;Dangerous&gt;) — or null for every other def.
        /// </summary>
        public static DangerousTechInfo InfoOf(ResearchProjectDef def)
        {
            if (def == null) return null;
            EnsureParents();
            EnsureStandalone();

            ResearchLoopTemplate parent;
            if (parents.TryGetValue(def.defName, out parent)) return parent.dangerous;

            DangerousTechInfo info;
            return standalone.TryGetValue(def.defName, out info) ? info : null;
        }

        /// <summary>True when the def is one of this mod's dangerous techs.</summary>
        public static bool IsDangerousDef(ResearchProjectDef def)
        {
            return InfoOf(def) != null;
        }

        /// <summary>
        /// The authored entry behind a STANDALONE ascension def, or null for every other def —
        /// loops, vanilla techs and nested dangerous techs alike. The four capstones are gated on this
        /// mod's level counters instead of vanilla's prerequisite test, so the seam that hands vanilla
        /// a lock reason is keyed on this and never on <see cref="InfoOf"/>: a nested dangerous tech's
        /// single prerequisite IS its parent chain, and there vanilla's own meaning is the right one.
        /// </summary>
        public static DangerousTechInfo StandaloneInfoOf(ResearchProjectDef def)
        {
            if (def == null) return null;
            EnsureStandalone();
            DangerousTechInfo info;
            return standalone.TryGetValue(def.defName, out info) ? info : null;
        }

        // ───────────────────── ascension techs (心胜于物 / 合成进化) ─────────────────────

        /// <summary>Effect kind of 心胜于物: no psyfocus cost, no entropy gain.</summary>
        public const string EffectAscension = "ascension";

        /// <summary>Effect kind of 合成进化: unlimited command range, mechs join the loop buff.</summary>
        public const string EffectSynthetic = "synthetic";

        /// <summary>
        /// Effect kind of 血肉苦弱 (formerly 血肉苦弱): the chains named in `zeroes` are treated as
        /// zeroed (no bleeding, no pain, no joy/rest/food need), plus no permanent injuries.
        /// </summary>
        public const string EffectCybernetic = "cybernetic";

        /// <summary>
        /// Effect kind of 设计进化 (formerly 设计进化): every body capacity +100%, authored stat
        /// multipliers (healing / immunity), and 远程必中 through RL_RangedAccuracy's zero.
        /// </summary>
        public const string EffectGene = "gene";

        private static Dictionary<string, DangerousTechInfo> effectInfos;

        /// <summary>
        /// The standalone tech authored with the given effect kind, or null when none is. Only one
        /// tech per kind can exist (Verify pins the authored set), so the first hit wins.
        /// </summary>
        public static DangerousTechInfo InfoForEffect(string effect)
        {
            if (string.IsNullOrEmpty(effect)) return null;

            if (effectInfos == null)
            {
                effectInfos = new Dictionary<string, DangerousTechInfo>(System.StringComparer.OrdinalIgnoreCase);
                EnsureStandalone();
                foreach (KeyValuePair<string, DangerousTechInfo> entry in standalone)
                {
                    DangerousTechInfo info = entry.Value;
                    if (info == null || string.IsNullOrEmpty(info.effect)) continue;
                    if (!effectInfos.ContainsKey(info.effect)) effectInfos.Add(info.effect, info);
                }
            }

            DangerousTechInfo result;
            return effectInfos.TryGetValue(effect, out result) ? result : null;
        }

        /// <summary>
        /// True when the standalone tech of that effect kind has been researched. Reads vanilla
        /// completion state (IsFinished), so a loaded save needs no migration — the same contract
        /// as <see cref="IsZeroed"/>.
        /// </summary>
        public static bool EffectResearched(string effect)
        {
            DangerousTechInfo info = InfoForEffect(effect);
            if (info == null) return false;

            // Fresh lookup, never cached: a language switch rebuilds every def object, and a
            // cached ResearchProjectDef would report IsFinished on the ORPHANED pre-rebuild
            // object — i.e. false — silently switching every ascension effect off (psyfocus
            // costs, command range, healing, needs top-up) until the next full restart.
            ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(info.defName);
            return def != null && def.IsFinished;
        }

        /// <summary>心胜于物 is done: the colony's psykers stop paying for their powers.</summary>
        public static bool Ascended
        {
            get { return EffectResearched(EffectAscension); }
        }

        /// <summary>
        /// 合成进化 is done: mechanitors command without a distance limit, and colony mechs join the
        /// loop buff.
        /// </summary>
        public static bool SyntheticAscended
        {
            get { return EffectResearched(EffectSynthetic); }
        }

        /// <summary>
        /// 血肉苦弱 is done: the body is machinery — no bleeding, no pain, no permanent injuries and
        /// no joy/rest/food needs.
        /// </summary>
        public static bool CyberAscended
        {
            get { return EffectResearched(EffectCybernetic); }
        }

        /// <summary>
        /// 设计进化 is done: the body has been rewritten — every capacity +100%, healing and
        /// immunity ×50, ranged attacks always hit.
        /// </summary>
        public static bool GeneAscended
        {
            get { return EffectResearched(EffectGene); }
        }

        /// <summary>
        /// True when a specific standalone tech is researched. Used by the buff builder, which walks
        /// the authored standalone list and applies whatever each one declares (capacity offset,
        /// stat multipliers, tooltip lines).
        /// </summary>
        public static bool IsResearched(DangerousTechInfo info)
        {
            return info != null && EffectResearched(info.effect);
        }

        /// <summary>
        /// The generated dangerous def of a parent loop, or null when the loop authors none (or the
        /// def is missing). Cached, including the null: a def that does not exist stays missing.
        /// </summary>
        public static ResearchProjectDef DefOf(ResearchLoopTemplate parent)
        {
            if (parent == null || parent.dangerous == null) return null;
            // Fresh lookup, never cached: a language switch clears and rebuilds every
            // DefDatabase, so a cached ResearchProjectDef goes stale — its IsFinished would read
            // the wrong object and every cost refreshed onto it would land on an orphan.
            return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(DefNameFor(parent));
        }

        /// <summary>
        /// True when the parent chain's coefficient is annihilated (0), not merely slowed:
        ///   - its own dangerous tech has been researched (none are authored since 2026-10-23), or
        ///   - a standalone ascension lists this chain in its `zeroes` (血肉苦弱 for the five
        ///     need/injury chains, 设计进化 for RL_RangedAccuracy).
        /// Reads vanilla completion state, so a loaded save needs no migration.
        /// </summary>
        public static bool IsZeroed(ResearchLoopTemplate parent)
        {
            ResearchProjectDef def = DefOf(parent);
            if (def != null && def.IsFinished) return true;
            return IsZeroedByAscension(parent);
        }

        /// <summary>
        /// True when a standalone tech has already zeroed this chain. Every standalone tech that
        /// authors a `zeroes` list contributes, each gated on its OWN completion: 血肉苦弱 zeroes the
        /// five need/injury chains, 设计进化 zeroes RL_RangedAccuracy (远程必中). Reading the
        /// authored lists per effect — instead of assuming one owner — is what lets the ascension be
        /// the only implementation of a zero once the per-loop dangerous techs are gone
        /// (2026-10-23: all six nested ones were removed on user request).
        /// Cheap on the hot path: the lists are turned into sets once and the completion flags
        /// short-circuit.
        /// </summary>
        public static bool IsZeroedByAscension(ResearchLoopTemplate parent)
        {
            if (parent == null || string.IsNullOrEmpty(parent.defName)) return false;

            EnsureZeroesByEffect();
            foreach (KeyValuePair<string, HashSet<string>> entry in zeroesByEffect)
            {
                if (entry.Value.Count == 0) continue;
                if (!EffectResearched(entry.Key)) continue;
                if (entry.Value.Contains(parent.defName)) return true;
            }
            return false;
        }

        /// <summary>effect kind -> the loop defNames that tech zeroes. Built once from authored data.</summary>
        private static Dictionary<string, HashSet<string>> zeroesByEffect;

        private static void EnsureZeroesByEffect()
        {
            if (zeroesByEffect != null) return;
            zeroesByEffect = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                if (info == null || string.IsNullOrEmpty(info.effect) || info.zeroes == null) continue;

                HashSet<string> set;
                if (!zeroesByEffect.TryGetValue(info.effect, out set))
                {
                    set = new HashSet<string>(System.StringComparer.Ordinal);
                    zeroesByEffect.Add(info.effect, set);
                }
                for (int i = 0; i < info.zeroes.Count; i++)
                {
                    if (!string.IsNullOrEmpty(info.zeroes[i])) set.Add(info.zeroes[i]);
                }
            }
        }

        /// <summary>True when the parent chain is complete: its single def reads as finished.</summary>
        private static bool ParentComplete(ResearchLoopTemplate parent)
        {
            if (parent == null) return false;
            ResearchProjectDef def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(parent.defName);
            return def != null && def.IsFinished;
        }

        /// <summary>
        /// Places every dangerous tech and its parent on the page. While the chain is incomplete
        /// the parent holds the authored coordinate; once the chain reads as finished the two swap,
        /// so the dangerous node sits exactly where the chain node was. Idempotent, and derived
        /// purely from state, so every level-up, load and settings change re-derives the same
        /// placement.
        /// </summary>
        public static void RefreshPlacement()
        {
            foreach (ResearchLoopTemplate template in ResearchLoopTemplates.All)
            {
                if (template == null || template.dangerous == null) continue;

                ResearchProjectDef parent = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(template.defName);
                ResearchProjectDef dangerous = DefOf(template);
                if (parent == null || dangerous == null) continue;

                if (ParentComplete(template))
                {
                    Place(dangerous, template.researchViewX, template.researchViewY);
                    Place(parent, ParkX(template), ParkY(template));
                }
                else
                {
                    Place(parent, template.researchViewX, template.researchViewY);
                    Place(dangerous, ParkX(template), ParkY(template));
                }
            }
        }

        /// <summary>Moves a def only when it is not already there, to keep the writes rare.</summary>
        private static void Place(ResearchProjectDef def, float x, float y)
        {
            if (def == null) return;
            if (def.researchViewX == x && def.researchViewY == y) return;
            def.researchViewX = x;
            def.researchViewY = y;
            def.ClearCachedData();
            ResearchLoopProgress.ClearCachedDescription(def);
        }

        /// <summary>Wraps a dangerous node title in a rich-text colour tag.</summary>
        public static string ColorLabel(string label, Color color)
        {
            if (string.IsNullOrEmpty(label)) return label;
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + label + "</color>";
        }

        // ───────────── research tab: the handover is invisible, the title is red ─────────────

        /// <summary>
        /// Vanilla's research tree greys locked projects but never hides them, so the handover
        /// needs a filter of its own:
        ///   - a nested dangerous def is invisible until its parent chain is complete (it is parked);
        ///   - once the chain IS complete, the parent node steps aside (it is parked too) — the
        ///     dangerous tech has taken its place.
        /// The four standalone ascensions are NOT filtered here: since 2026-11-04 (user directive)
        /// they sit on their authored coordinates from the first game, and whether they can be
        /// researched is <see cref="Patch_CanStartNow"/>'s business (each prerequisite chain at
        /// `prereqLevel`), not this list's.
        /// The vanilla cached list is never mutated — a filtered copy is handed back, so a later
        /// rebuild cannot lose a node.
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_Research), "VisibleResearchProjects", MethodType.Getter)]
        public static class Patch_VisibleResearchProjects
        {
            public static void Postfix(ref List<ResearchProjectDef> __result)
            {
                List<ResearchProjectDef> list = __result;
                if (list == null || list.Count == 0) return;

                bool anyHidden = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (ShouldHide(list[i]))
                    {
                        anyHidden = true;
                        break;
                    }
                }
                if (!anyHidden) return;

                var filtered = new List<ResearchProjectDef>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (ShouldHide(list[i])) continue;
                    filtered.Add(list[i]);
                }
                __result = filtered;
            }

            private static bool ShouldHide(ResearchProjectDef def)
            {
                // A dangerous tech stays out of sight until the chain it replaces is complete.
                ResearchLoopTemplate parent = ParentOf(def);
                if (parent != null)
                {
                    if (!ParentComplete(parent)) return true;
                    // ...and it disappears again once 血肉苦弱 has already bought its effect: the
                    // coefficient is 0 either way, so showing a 10万 node that changes nothing
                    // would just be a trap.
                    return IsZeroedByAscension(parent);
                }

                // The four standalone ascensions are drawn from the first game (user, 2026-11-04).
                // Hiding and unlocking are separate questions: whether the node can be researched is
                // Patch_CanStartNow's job (each prerequisite chain at info.prereqLevel), so nothing
                // here hides them.
                if (InfoOf(def) != null) return false;

                // A completed chain that authors a dangerous tech has handed its spot over, so it
                // steps aside; chains without one keep showing as finished, like any vanilla tech.
                ResearchLoopTemplate loop = ResearchLoopProgress.TemplateOf(def);
                return loop != null && loop.dangerous != null && def.IsFinished;
            }
        }

        /// <summary>
        /// The ascension gate (2026-11-04, user 「飞升的话要求前置技能10级，不要现在的2级」): a node is
        /// researchable once EVERY chain it names has completed at least `prereqLevel` levels.
        ///
        /// This ANDs onto vanilla's own answer, so it can only ever lock a node further — a finished
        /// ascension, a missing research bench or an unmet techprint still refuse it exactly as before.
        /// The levels come from this mod's counter (GameComponent_ResearchLoops.GetLevel, the same
        /// number the save stores and the node label shows), never from the prereq def's IsFinished:
        /// for a loop def that flag compares ResearchManager's stored progress against a baseCost this
        /// mod rewrites on every level-up, and in game it read "finished" two levels in.
        /// </summary>
        [HarmonyPatch(typeof(ResearchProjectDef), "CanStartNow", MethodType.Getter)]
        public static class Patch_CanStartNow
        {
            public static void Postfix(ResearchProjectDef __instance, ref bool __result)
            {
                if (!__result) return;

                DangerousTechInfo info = InfoOf(__instance);
                if (info == null) return;

                __result = PrereqLevelsMet(info);
            }
        }

        /// <summary>
        /// True when every prerequisite chain of this ascension has reached its authored level.
        /// The rule itself is pure (ResearchLoopPlan.AscensionLevelsMet, unit-tested in Verify); this
        /// is the Verse side that feeds it live levels.
        /// </summary>
        public static bool PrereqLevelsMet(DangerousTechInfo info)
        {
            return ResearchLoopPlan.AscensionLevelsMet(info, GameComponent_ResearchLoops.GetLevel);
        }

        /// <summary>
        /// The four capstones carry no vanilla prerequisites, so vanilla's own answer here is always
        /// "completed" — yet MainTabWindow_Research.DrawStartButton reads THIS property for the lock
        /// reason. Without this patch a greyed ascension shows a locked button with no reason at all,
        /// and vanilla logs "Research ... locked but no reasons given" once per def. Answering with
        /// this mod's level check hands vanilla its own "前置研究项目未完成" line, and it says 未完成
        /// at the authored prereqLevel instead of implying 练满 50 级.
        ///
        /// CanStartNow ANDs this property, so <see cref="Patch_CanStartNow"/> and this one compute the
        /// same value for these defs; the load-time self-check in Patches.cs keeps the def
        /// prerequisite-free, which is what stops vanilla's "every prerequisite IsFinished" (= a chain
        /// at 满级) from becoming the rule again. Nested dangerous techs are NOT touched here — their
        /// single prerequisite IS the parent chain, and vanilla's answer is what that branch means.
        /// </summary>
        [HarmonyPatch(typeof(ResearchProjectDef), "PrerequisitesCompleted", MethodType.Getter)]
        public static class Patch_PrerequisitesCompleted
        {
            public static void Postfix(ResearchProjectDef __instance, ref bool __result)
            {
                DangerousTechInfo info = StandaloneInfoOf(__instance);
                if (info == null) return;

                __result = PrereqLevelsMet(info);
            }
        }

        /// <summary>
        /// The panel block the user asked for on 2026-11-04 (「把飞升的前置条件ui做成这样，更加符合原版」,
        /// screenshot of vanilla's 「前置研究项目:」 list attached), on the route chosen over borrowing
        /// vanilla's list: this mod draws the block and the def stays prerequisite-free.
        ///
        /// Why not let vanilla draw it: vanilla colours each row by `prerequisite.IsFinished`, and for a
        /// loop def that reads true only at 练满 50 级, not at the authored 10 — the block would stay
        /// all-red on a node that researches fine. The same list would also draw connector lines into a
        /// capstone parked at the head of its row, and ResearchManager.FinishProject would recursively
        /// complete the four chains, gifting a level to each. Curing those three means editing vanilla's
        /// drawing and its completion flow, which the same user had already ruled out (「不要动ui」).
        ///
        /// So vanilla's method runs first and returns immediately here (its own lists are empty), and
        /// this postfix lays out the same block in the same place: same title key, same 6f indent, same
        /// "- " rows, same two colours, same "plain white once the project itself is finished" rule —
        /// with this mod's level check deciding green. Every other tech's panel is untouched: the first
        /// line returns for anything that is not one of the four capstones.
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_Research), "DrawResearchPrerequisites")]
        public static class Patch_DrawAscensionPrerequisites
        {
            public static void Postfix(Rect rect, ref float y, ResearchProjectDef project)
            {
                DangerousTechInfo info = StandaloneInfoOf(project);
                if (info == null) return;

                List<ResearchLoopPlan.AscensionPrereqLine> lines = ResearchLoopPlan.AscensionPrereqLines(
                    info, GameComponent_ResearchLoops.GetLevel, ResearchLoopProgress.PrereqDisplayName);
                if (lines.Count == 0) return;

                Widgets.Label(rect, ref y, string.Format("{0}:", "Prerequisites".Translate()));
                rect.xMin += 6f;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!project.IsFinished)
                        GUI.color = lines[i].Met ? Color.green : ColorLibrary.RedReadable;
                    Widgets.Label(rect, ref y, "- " + lines[i].Text);
                }
                GUI.color = Color.white;
            }
        }

        /// <summary>Red title for dangerous nodes in the research tree (info panel excluded).</summary>
        [HarmonyPatch(typeof(MainTabWindow_Research), "GetLabel")]
        public static class Patch_GetLabel
        {
            public static void Postfix(ResearchProjectDef r, ref string __result)
            {
                DangerousTechInfo info = InfoOf(r);
                if (info == null) return;
                __result = ColorLabel(__result, ColorFor(info));
            }
        }
    }
}
