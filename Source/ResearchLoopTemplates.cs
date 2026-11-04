using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// One research-loop definition as authored in Data/ResearchLoopTemplates.xml.
    ///
    /// This is plain data, NOT a Def: the file lives outside Defs/ so RimWorld never loads it
    /// into DefDatabase. PatchOperation_GenerateLoopDefs reads it while applying XML patches
    /// (before any def exists — see its header note) and emits the real ResearchProjectDef.
    /// It is also the single authoring source for costs, coordinates and all display text:
    /// the old design kept costs/labels/descriptions duplicated in a second hand-written
    /// Defs file, which drifted and never got a chance to be checked.
    /// </summary>
    public class ResearchLoopTemplate
    {
        /// <summary>Stable id, e.g. "RL_Mining". Join key for the counter, the buff hediff and the gene.</summary>
        public string defName;

        /// <summary>
        /// Display name: the effect itself, e.g. "远程伤害" / "护甲值" (user directive 2026-10-23:
        /// 「默认名称改为直接效果，删除群星风格的那些命名」). This replaced the old three-tier
        /// naming — a Stellaris-style conceptName ("动能弹道强化技术"), a short conceptShort
        /// ("远程武器") that only the docs ever read, and a separate conciseName that required a
        /// settings toggle to show. One authored name now serves every surface: node label,
        /// completion dialog, AutoLoop log lines.
        /// </summary>
        public string name;

        /// <summary>
        /// English counterpart of every user-facing string (2026-10-23, user directive 「加入英文版，
        /// 除语言为中文外其余语言对应均为英文」). Chinese (simplified or traditional) keeps the
        /// Chinese fields; every other language renders these. Kept as parallel fields rather than
        /// RimWorld Keyed translations because these strings are baked into runtime-generated defs,
        /// where DefInjected/Keyed injection does not reach.
        /// </summary>
        public string nameEn;

        public string descriptionEn;

        public string effectTextEn;

        public string descriptionFormulaEn;

        public string capstoneNoteEn;

        /// <summary>Concept blurb: what the loop affects and why the chain is shaped as it is.</summary>
        public string description;

        /// <summary>Per-level status text; {0}/{1}/{2}/{3} are filled by ResearchLoopPlan.</summary>
        public string descriptionFormula;

        /// <summary>Effect sentence with {1} = this level's magnitude.</summary>
        public string effectText;

        /// <summary>Chain length: the last level the loop offers.</summary>
        public int maxLevel;

        /// <summary>Authored cost of level 1; the per-level curve scales from it (LoopMath.LevelCost).</summary>
        public float baseCost;

        /// <summary>
        /// Which cost curve this chain runs on: "normal" (+50%/级) | "strong" (+100%/级) |
        /// "power" (+150%/级). Authored per loop via the "costTier" attribute so one chain can be
        /// cheap to enter but steep at the top (or the reverse) without touching baseCost.
        /// A missing, empty or unrecognised value reads as <see cref="LoopMath.TierNormal"/> —
        /// the curve the mod shipped before tiers existed. <see cref="CostSlope"/> resolves it.
        /// </summary>
        public string costTier;

        /// <summary>
        /// The per-level slope this chain's costs escalate at, resolved from
        /// <see cref="costTier"/> (unknown values degrade to the normal curve). This is the single
        /// number every cost consumer must pass to <see cref="LoopMath.LevelCost"/>.
        /// </summary>
        public float CostSlope
        {
            get { return LoopMath.CostSlope(costTier); }
        }

        /// <summary>Per-level magnitude: 0.05 means +5%/level (or the flat step in 'add' mode).</summary>
        public float perLevel;

        /// <summary>True for lower-is-better stats, which invert the factor direction.</summary>
        public bool lowerIsBetter;

        /// <summary>Research-tree X coordinate inside this mod's own tab (unique per loop).</summary>
        public float researchViewX;

        /// <summary>Research-tree Y coordinate inside this mod's own tab (unique per loop).</summary>
        public float researchViewY;

        /// <summary>
        /// Vanilla SkillDef this loop levels up (delivery="skill" only): one point per completed
        /// level, applied on read by Patch_SkillLevel. Empty for stat/patch loops.
        ///
        /// Kept as the FIRST entry of <see cref="skills"/> so every single-skill consumer (the
        /// verifier's "levels a vanilla skill" check, older notes) keeps working. Read
        /// <see cref="skills"/> when the loop may feed more than one skill.
        /// </summary>
        public string skill;

        /// <summary>
        /// Every vanilla SkillDef this loop levels up, from the comma-separated 'skills' attribute
        /// (falling back to the single 'skill'). One point per completed level goes to EACH of them,
        /// which is how 技能等级 covers all eight work skills with one chain.
        ///
        /// Never null after parsing (empty list for non-skill loops), so callers can iterate it
        /// without a null check.
        /// </summary>
        public List<string> skills;

        /// <summary>True when this loop grants a point to <paramref name="skillDefName"/>.</summary>
        public bool FeedsSkill(string skillDefName)
        {
            if (string.IsNullOrEmpty(skillDefName) || skills == null) return false;

            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] == skillDefName) return true;
            }
            return false;
        }

        /// <summary>Original stat defName, for the Verify cross-check (not consumed at runtime).</summary>
        public string statDefName;

        /// <summary>1-based level at which the stat reaches its hard ceiling. 0 = no ceiling.</summary>
        public int capLevel;

        /// <summary>"factor" | "add" | "tiers", for the Verify cross-check and the effect text.</summary>
        public string mode;

        /// <summary>The patch point that delivers this loop, for the Verify cross-check.</summary>
        public string delivery;

        /// <summary>Prerequisite research defName. All loops gate on MultiAnalyzer by design.</summary>
        public string prerequisite;

        /// <summary>
        /// Every StatDef this loop's StatPart is mounted on, parsed from the comma-separated
        /// 'stats' attribute (falling back to the single 'stat'). Armor is the one loop that
        /// covers more than one stat (Sharp/Blunt/Heat); before this list existed, that 1-to-3
        /// mapping lived only in the hand-written StatPart patch file and was invisible in the
        /// template — a silent drift hazard the generator now cannot have.
        /// </summary>
        public List<string> targetStats;

        /// <summary>Lower clamp for 'add' mode; null means unbounded below.</summary>
        public float? clampMin;

        /// <summary>Upper clamp for 'add' mode; null means unbounded above.</summary>
        public float? clampMax;

        /// <summary>True when the chain is deliberately short because the stat hits a ceiling.</summary>
        public bool capstone;

        /// <summary>One-line explanation shown on the last level of a capped chain.</summary>
        public string capstoneNote;

        /// <summary>
        /// When true, this loop's EFFECT never scales with the player's effect multiplier, and
        /// instead its RESEARCH COST scales inversely with that multiplier (cost ÷ multiplier).
        /// Used by the item-quality loop: quality tiers are discrete (Awful..Legendary), so scaling
        /// the per-level rate is meaningless — the benefit is fixed at the authored tier count, and
        /// the multiplier only makes the research cheaper. Authored via the
        /// "costInverseEffectScale" attribute; defaults to false.
        /// </summary>
        public bool costInverseEffectScale;

        /// <summary>
        /// A few pawn-level quantities have NO StatDef to ride, so a stat-mounted loop cannot
        /// reach them (1.6 ships neither a PainFactor nor a bleeding-rate StatDef — verified in
        /// Data/Core/Defs/Stats). They are fields on the buff hediff's own stage instead, which is
        /// why this loop names one here instead of a StatDef:
        ///
        ///   "painFactor"       -> HediffStage.painFactor, read by Hediff.PainFactor and multiplied
        ///                         into HediffSet.PainTotal (i.e. it scales pain from EXISTING wounds).
        ///   "totalBleedFactor" -> HediffStage.totalBleedFactor, multiplied into
        ///                         HediffSet.BleedRateTotal (bleed speed of every untended wound).
        ///
        /// Empty for every stat loop. Authored via the "stageField" attribute; null or empty means
        /// "no stage field", so the loop behaves exactly as before.
        /// </summary>
        public string stageField;

        /// <summary>Stage field names this loop may target (see <see cref="stageField"/>).</summary>
        public const string StagePainFactor = "painFactor";

        public const string StageTotalBleedFactor = "totalBleedFactor";

        /// <summary>True when this loop delivers through a hediff-stage field instead of a StatDef.</summary>
        public bool UsesStageField
        {
            get { return !string.IsNullOrEmpty(stageField); }
        }

        /// <summary>
        /// True when this loop's effect means something to a mechanoid (default). Since 合成进化
        /// puts the loop buff on colony mechs, the few chains that drive a biological need or a
        /// psyfocus stat are authored with mechApplies="false" so a mech's buff tooltip never
        /// claims 「娱乐值下降速率 ×0.08」. Copy only: the stat factors stay on the stage (a factor
        /// on a stat a mech never reads is inert), so this flag cannot change gameplay.
        /// </summary>
        public bool mechApplies = true;

        /// <summary>
        /// The dangerous tech authored on this loop (null when the loop has none): a one-shot
        /// research project that appears once the chain is complete and drives this loop's
        /// coefficient to literal zero. Authored as a &lt;dangerous&gt; child element.
        /// Always null since 2026-10-23: the six nested techs were deleted on user request and
        /// the parser keeps the field only so re-adding one needs no code change. A coefficient
        /// zero now comes from a STANDALONE tech's `zeroes` list instead.
        /// </summary>
        public DangerousTechInfo dangerous;
    }

    /// <summary>
    /// One dangerous tech, authored in either of two shapes:
    ///
    /// 1. NESTED — a &lt;dangerous&gt; child of a &lt;Loop&gt;:
    ///      &lt;dangerous cost="100000"&gt;&lt;label&gt;告别休息&lt;/label&gt;…&lt;/dangerous&gt;
    ///    It appears once that chain is complete and drives the chain's coefficient to literal
    ///    zero. defName = parent + <see cref="ResearchLoopsIds.DangerousSuffix"/>, prerequisite =
    ///    the parent loop def, and it authors NO coordinate: the node takes over the parent's
    ///    position on the page at runtime (see DangerousTech.RefreshPlacement).
    ///
    /// 2. STANDALONE — a top-level &lt;Dangerous&gt; element:
    ///      &lt;Dangerous defName="RL_MindOverMatter" cost="500000"
    ///                 prerequisite="RL_PsyEntropyMax,RL_PsyEntropyRecovery,…"
    ///                 researchViewX="5.8" researchViewY="9.4" effect="ascension"&gt;
    ///    A capstone of SEVERAL chains at once. It owns its coordinate (there is no parent node to
    ///    inherit), only shows once every prerequisite def reads as finished, and its effect is
    ///    whatever its authored `effect` kind says — "ascension" is the psyfocus / entropy waiver
    ///    (Source/PsychicAscension.cs).
    /// </summary>
    public class DangerousTechInfo
    {
        /// <summary>Node title of the dangerous tech, e.g. "告别休息" / "心胜于物".</summary>
        public string label;

        /// <summary>English node title (see <see cref="ResearchLoopTemplate.nameEn"/> for the rule).</summary>
        public string labelEn;

        /// <summary>What the dangerous tech does, shown on the node.</summary>
        public string description;

        /// <summary>English description, including the English flavour line.</summary>
        public string descriptionEn;

        /// <summary>One-shot research cost (authored; the cost slider still applies).</summary>
        public float cost;

        /// <summary>
        /// The generated def's defName: authored for a standalone tech, derived as
        /// parent + <see cref="ResearchLoopsIds.DangerousSuffix"/> for a nested one.
        /// </summary>
        public string defName;

        /// <summary>
        /// Every research def this node is gated on. Nested techs carry exactly their parent
        /// chain; a standalone tech carries the authored list. Since 2026-11-04 this list is NOT
        /// written to the generated def's &lt;prerequisites&gt; any more: the standalone gate is
        /// this mod's own level check (<see cref="prereqLevel"/>), because vanilla's prerequisite
        /// test reads IsFinished, which for a loop def compares accumulated progress against the
        /// baseCost this mod keeps re-projecting.
        /// </summary>
        public List<string> prerequisites;

        /// <summary>
        /// Level EVERY chain in <see cref="prerequisites"/> must have reached before a STANDALONE
        /// tech can be researched (authored `prereqLevel`, 10 for all four ascensions per the user
        /// on 2026-11-04). Read from this mod's own level counters, so it is independent of
        /// vanilla's finished/progress bookkeeping. Non-positive = malformed: the entry is dropped
        /// rather than generated as a node anybody can click. Nested techs leave it 0.
        /// </summary>
        public int prereqLevel;

        /// <summary>Page coordinate — standalone only; a nested tech is parked above its parent.</summary>
        public float researchViewX;

        public float researchViewY;

        /// <summary>
        /// Effect kind of a STANDALONE dangerous tech, as authored in `effect=`. Nested techs leave
        /// it null (their effect is always "zero the parent chain").
        ///   "ascension" — 心胜于物: no psyfocus cost, no entropy gain (Source/PsychicAscension.cs).
        ///   "synthetic" — 合成进化: unlimited mechanitor command range, and mechs join the loop
        ///                 buff (Source/SyntheticAscension.cs, LoopEligibility.IsLoopTarget).
        /// Compared case-insensitively; see <see cref="IsEffect"/>.
        /// </summary>
        public string effect;

        /// <summary>
        /// Loop defNames this tech drives to literal zero, authored as `zeroes` (cybernetic / gene).
        /// 血肉苦弱 names the five need/injury chains; 设计进化 names RL_RangedAccuracy for 远程必中.
        /// <see cref="DangerousTech.IsZeroed"/> treats them as zeroed while THIS tech is researched.
        /// Since 2026-10-23 the per-loop &lt;dangerous&gt; elements are all gone, so this list is the
        /// only implementation of those zeros — and IsZeroedByAscension walks every researched
        /// tech's list rather than assuming 血肉苦弱 owns them (that assumption silently dropped
        /// 设计进化's 远程必中 before the fix).
        /// </summary>
        public List<string> zeroes;

        /// <summary>
        /// Authored offset added to EVERY PawnCapacityDef on the buff (1 = all capacities +100%).
        /// 0 = no capacity change. 设计进化's 「操作能力之类的全部加100%」.
        /// </summary>
        public float capacityOffset;

        /// <summary>Authored multiplicative stat modifiers on the buff (设计进化's ×50 healing/immunity).</summary>
        public List<DangerousStatFactor> statFactors;

        /// <summary>Authored extra lines for the buff tooltip (one per effect that has no chain).</summary>
        public List<DangerousBuffLine> buffLines;

        /// <summary>
        /// HediffDef this ascension shows in the health tab, authored as `hediffDef`
        /// (2026-10-23 user directive: 「把四个飞升效果单拎出来，健康栏中独立于循环科技效果」).
        /// Each of the four gets its OWN hediff, so the player can tell an ascension's effect from
        /// a chain's effect at a glance; the loop buff keeps only the chain lines. Null for a
        /// nested dangerous tech (it never had a hediff of its own).
        /// </summary>
        public string hediffDef;

        /// <summary>True when this tech declares the given effect kind.</summary>
        public bool IsEffect(string kind)
        {
            return !string.IsNullOrEmpty(effect) && string.Equals(effect, kind, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when authored as a top-level &lt;Dangerous&gt; element.</summary>
        public bool standalone;
    }

    /// <summary>
    /// One authored stat multiplier a standalone dangerous tech puts on the buff, e.g.
    /// &lt;li&gt;&lt;stat&gt;InjuryHealingFactor&lt;/stat&gt;&lt;value&gt;50&lt;/value&gt;&lt;/li&gt;.
    /// Kept as data so the numbers can be tuned in XML instead of in code.
    /// </summary>
    public class DangerousStatFactor
    {
        /// <summary>Vanilla StatDef defName.</summary>
        public string stat;

        /// <summary>Multiplier applied to that stat (factor mode, so 50 = ×50).</summary>
        public float value = 1f;
    }

    /// <summary>
    /// One authored buff-tooltip line. Effects with no chain and no stat of their own to quote
    /// (设计进化's capacity boost, 心胜于物's waiver) are written here so the copy stays in the
    /// template file like every other string.
    /// </summary>
    public class DangerousBuffLine
    {
        /// <summary>The line itself, printed as-is (same "effect only" voice as the chain lines).</summary>
        public string text;

        /// <summary>English line, authored as `textEn` on the same &lt;li&gt;.</summary>
        public string textEn;

        /// <summary>False when the line means nothing on a mechanoid (`mechs="false"`).</summary>
        public bool forMech = true;
    }

    /// <summary>
    /// Everything one template file holds: the loop chains plus the standalone dangerous techs.
    /// Kept as one object so a single read of the file feeds both lists (and so the verifier can
    /// assert them against each other).
    /// </summary>
    public class ResearchLoopTemplateFile
    {
        public readonly List<ResearchLoopTemplate> loops = new List<ResearchLoopTemplate>();

        public readonly List<DangerousTechInfo> dangerous = new List<DangerousTechInfo>();
    }

    /// <summary>
    /// Reads the mod's own authored template data. The def generator runs inside a
    /// PatchOperation, which receives only the XmlDocument, so the file is located through
    /// this mod's content pack (Verified: Verse.LoadedModManager.RunningModsListForReading is
    /// populated before patches run, and ModContentPack.RootDir gives the mod folder).
    ///
    /// NOTE what is legitimately readable at patch time and what is not: the mod list and the
    /// mod folder are available, but DefDatabase is EMPTY (patches run before defs are
    /// parsed). Nothing in this class or the generator may resolve a def at load-patch time —
    /// the predecessor did and silently generated nothing.
    /// </summary>
    public static class ResearchLoopTemplates
    {
        public const string RelativePath = "Data/ResearchLoopTemplates.xml";

        private static List<ResearchLoopTemplate> cached;
        private static Dictionary<string, ResearchLoopTemplate> cachedByName;
        private static List<DangerousTechInfo> cachedDangerous;

        public static List<ResearchLoopTemplate> All
        {
            get
            {
                if (cached == null) Load();
                return cached;
            }
        }

        /// <summary>
        /// The standalone dangerous techs (top-level &lt;Dangerous&gt; elements): capstones that
        /// belong to several chains at once and own their page coordinate. Nested &lt;dangerous&gt;
        /// techs are NOT here — they live on their loop template, because they replace that node.
        /// </summary>
        public static List<DangerousTechInfo> DangerousTechs
        {
            get
            {
                if (cachedDangerous == null) Load();
                return cachedDangerous;
            }
        }

        public static ResearchLoopTemplate Get(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            if (cachedByName == null) Load();
            ResearchLoopTemplate template;
            return cachedByName.TryGetValue(defName, out template) ? template : null;
        }

        private static void Load()
        {
            string path = FindTemplatePath();

            if (path == null || !File.Exists(path))
            {
                Log.Error("[ResearchLoops] template file not found (packageId=" + ResearchLoopsIds.PackageId +
                          ", rel=" + RelativePath + "). Looked next to Assemblies/ResearchLoops.dll and in " +
                          "every running mod folder claiming that packageId (or its '_steam' form). " +
                          "No loop defs will be generated, so the research page will be empty.");
                Cache(new List<ResearchLoopTemplate>(), new List<DangerousTechInfo>());
                return;
            }

            ResearchLoopTemplateFile file = LoadFile(path);
            if (file.loops.Count == 0)
            {
                Log.Error("[ResearchLoops] parsed 0 loop templates from " + path +
                          ". No loop defs will be generated.");
            }
            else
            {
                Log.Message("[ResearchLoops] read " + file.loops.Count + " loop templates and " +
                            file.dangerous.Count + " standalone dangerous tech(s) from " + path);
            }
            Cache(file.loops, file.dangerous);
        }

        /// <summary>
        /// Locates Data/ResearchLoopTemplates.xml inside THIS mod's own folder.
        ///
        /// The packageId match this used to rely on alone is not safe, for two independent reasons
        /// that both ended in "no research options" (user report, 2026-10-31):
        ///
        ///  1. RimWorld appends "_steam" to the packageId of a Steam Workshop copy when a local copy
        ///     with the same packageId is also installed, so ModsConfig held
        ///     "liaoqingshan.researchloops_steam" while the constant said the plain id and the
        ///     exact string comparison matched nothing.
        ///  2. The compiled constant can drift from About/About.xml (it did: the workshop rename in
        ///     §36 touched only the XML), and then nothing matches at all.
        ///
        /// Either way the generator quietly loaded zero templates. So the assembly's own location is
        /// tried FIRST — it needs no id at all and works for local and workshop installs alike — and
        /// the packageId match stays as a fallback that tolerates the "_steam" suffix.
        /// </summary>
        private static string FindTemplatePath()
        {
            string relative = RelativePath.Replace('/', Path.DirectorySeparatorChar);

            try
            {
                string assemblyPath = typeof(ResearchLoopTemplates).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    DirectoryInfo assembliesFolder = Directory.GetParent(assemblyPath);      // <mod>/Assemblies
                    DirectoryInfo modFolder = assembliesFolder == null ? null : assembliesFolder.Parent;
                    if (modFolder != null)
                    {
                        string own = Path.Combine(modFolder.FullName, relative);
                        if (File.Exists(own)) return own;
                    }
                }
            }
            catch (Exception)
            {
                // Assembly.Location is empty in some hosting setups; the id match below covers it.
            }

            foreach (ModContentPack pack in LoadedModManager.RunningModsListForReading)
            {
                if (pack == null || !IsOwnPackageId(pack.PackageId)) continue;
                string candidate = Path.Combine(pack.RootDir, relative);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static bool IsOwnPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            if (string.Equals(packageId, ResearchLoopsIds.PackageId, StringComparison.OrdinalIgnoreCase))
                return true;

            // The disambiguated name RimWorld gives a workshop copy that collides with a local one.
            return string.Equals(packageId, ResearchLoopsIds.PackageId + "_steam", StringComparison.OrdinalIgnoreCase);
        }

        private static void Cache(List<ResearchLoopTemplate> templates, List<DangerousTechInfo> dangerous)
        {
            cached = templates;
            cachedDangerous = dangerous;
            cachedByName = new Dictionary<string, ResearchLoopTemplate>(templates.Count, StringComparer.Ordinal);
            foreach (ResearchLoopTemplate template in templates)
            {
                if (!string.IsNullOrEmpty(template.defName) && !cachedByName.ContainsKey(template.defName))
                {
                    cachedByName.Add(template.defName, template);
                }
            }
        }

        /// <summary>
        /// Parses a template file: both the &lt;Loop&gt; chains and the standalone &lt;Dangerous&gt;
        /// techs. Deliberately free of any Verse reference so this exact parser can also run inside
        /// the verification project, which must not load Assembly-CSharp: a method that merely calls
        /// Log would need the whole game assembly resolved at JIT time.
        /// Entries that are not usable are skipped, so a short result is the caller's failure signal.
        /// </summary>
        public static ResearchLoopTemplateFile LoadFile(string path)
        {
            var file = new ResearchLoopTemplateFile();
            var doc = new XmlDocument();
            doc.Load(path);

            XmlNodeList nodes = doc.DocumentElement != null ? doc.DocumentElement.ChildNodes : null;
            if (nodes == null) return file;

            foreach (XmlNode node in nodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;

                // A standalone dangerous tech is a sibling of the loops, not a loop: it has no
                // levels, no counter and no buff entry, so it must never reach the loop list.
                if (node.Name == "Dangerous")
                {
                    DangerousTechInfo info = FromDangerousNode(node);
                    if (info != null) file.dangerous.Add(info);
                    continue;
                }

                ResearchLoopTemplate template = FromNode(node);
                if (template == null) continue;

                if (string.IsNullOrEmpty(template.defName) || template.maxLevel <= 0 || template.baseCost <= 0f)
                {
                    // Skipped silently here; Load() reports when the parsed count comes up short,
                    // and Tests/Verify.cs fails loudly on any authoring regression.
                    continue;
                }
                file.loops.Add(template);
            }
            return file;
        }

        /// <summary>
        /// Loop-only entry point, kept for callers that predate standalone dangerous techs (and for
        /// the verifier, which asserts the chain set independently of them).
        /// </summary>
        public static List<ResearchLoopTemplate> LoadFromFile(string path)
        {
            return LoadFile(path).loops;
        }

        /// <summary>
        /// Builds one standalone dangerous tech from its top-level &lt;Dangerous&gt; element.
        /// Returns null when it is malformed (no defName/label/description, non-positive cost, no
        /// prerequisite at all, or a non-positive prereqLevel), so a half-authored entry degrades
        /// to "no tech" instead of generating a def nobody can ever unlock — or one everybody can;
        /// Tests/Verify.cs asserts the authored set.
        /// </summary>
        private static DangerousTechInfo FromDangerousNode(XmlNode node)
        {
            var info = new DangerousTechInfo
            {
                defName = Attr(node, "defName"),
                label = ChildText(node, "label"),
                labelEn = ChildText(node, "labelEn"),
                description = ChildText(node, "description"),
                descriptionEn = ChildText(node, "descriptionEn"),
                cost = ParseFloat(Attr(node, "cost"), 0f),
                prerequisites = ParseList(Attr(node, "prerequisite")),
                prereqLevel = ParseInt(Attr(node, "prereqLevel"), 0),
                researchViewX = ParseFloat(Attr(node, "researchViewX"), 0f),
                researchViewY = ParseFloat(Attr(node, "researchViewY"), 0f),
                effect = Attr(node, "effect"),
                zeroes = ParseList(Attr(node, "zeroes")),
                capacityOffset = ParseFloat(Attr(node, "capacityOffset"), 0f),
                statFactors = ParseStatFactors(node),
                buffLines = ParseBuffLines(node),
                hediffDef = Attr(node, "hediffDef"),
                standalone = true
            };

            if (string.IsNullOrEmpty(info.defName) || string.IsNullOrEmpty(info.label) ||
                string.IsNullOrEmpty(info.description) || info.cost <= 0f ||
                info.prerequisites.Count == 0 || info.prereqLevel <= 0)
            {
                return null;
            }
            return info;
        }

        /// <summary>Splits a comma-separated defName list (prerequisites); blank entries dropped.</summary>
        private static List<string> ParseList(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// Parses an optional &lt;statFactors&gt; block: one stat multiplier per &lt;li&gt;. A malformed
        /// entry is skipped (no stat name, or a non-positive/non-finite value), so a typo degrades
        /// to "that one multiplier is missing" instead of poisoning the buff; Verify pins the
        /// authored set.
        /// </summary>
        private static List<DangerousStatFactor> ParseStatFactors(XmlNode node)
        {
            var result = new List<DangerousStatFactor>();
            XmlNode list = node["statFactors"];
            if (list == null) return result;

            foreach (XmlNode li in list.ChildNodes)
            {
                if (li.NodeType != XmlNodeType.Element) continue;

                string stat = ChildText(li, "stat");
                float value = ParseFloat(ChildText(li, "value"), 0f);
                if (string.IsNullOrEmpty(stat) || value <= 0f) continue;

                result.Add(new DangerousStatFactor { stat = stat.Trim(), value = value });
            }
            return result;
        }

        /// <summary>
        /// Parses an optional &lt;buffLines&gt; block: one tooltip line per &lt;li&gt;. `mechs="false"`
        /// keeps the line out of a mechanoid's copy (a mech has no immunity and does not heal).
        /// </summary>
        private static List<DangerousBuffLine> ParseBuffLines(XmlNode node)
        {
            var result = new List<DangerousBuffLine>();
            XmlNode list = node["buffLines"];
            if (list == null) return result;

            foreach (XmlNode li in list.ChildNodes)
            {
                if (li.NodeType != XmlNodeType.Element) continue;

                string text = li.InnerText != null ? li.InnerText.Trim() : null;
                if (string.IsNullOrEmpty(text)) continue;

                result.Add(new DangerousBuffLine
                {
                    text = text,
                    textEn = Attr(li, "textEn"),
                    forMech = !string.Equals(Attr(li, "mechs"), "false", StringComparison.OrdinalIgnoreCase)
                });
            }
            return result;
        }

        /// <summary>Builds one template from its &lt;Loop&gt; element.</summary>
        private static ResearchLoopTemplate FromNode(XmlNode node)
        {
            string statsAttr = Attr(node, "stats");
            string statAttr = Attr(node, "stat");
            string skillAttr = Attr(node, "skill");
            List<string> skillList = ParseSkills(Attr(node, "skills"), skillAttr);
            string defName = Attr(node, "defName");

            return new ResearchLoopTemplate
            {
                defName = defName,
                maxLevel = ParseInt(ChildText(node, "maxLevel"), 0),
                capLevel = ParseInt(ChildText(node, "capLevel"), 0),
                baseCost = ParseFloat(ChildText(node, "baseCost"), 0f),
                costTier = Attr(node, "costTier"),
                perLevel = ParseFloat(ChildText(node, "perLevel"), 0.05f),
                researchViewX = ParseFloat(Attr(node, "researchViewX"), 1f),
                researchViewY = ParseFloat(Attr(node, "researchViewY"), 1f),
                lowerIsBetter = string.Equals(Attr(node, "lowerIsBetter"), "true", StringComparison.OrdinalIgnoreCase),
                name = ChildText(node, "name"),
                nameEn = ChildText(node, "nameEn"),
                descriptionEn = ChildText(node, "descriptionEn"),
                effectTextEn = ChildText(node, "effectTextEn"),
                descriptionFormulaEn = ChildText(node, "descriptionFormulaEn"),
                description = ChildText(node, "description"),
                effectText = ChildText(node, "effectText"),
                descriptionFormula = ChildText(node, "descriptionFormula"),
                capstoneNote = Attr(node, "capNote"),
                capstoneNoteEn = Attr(node, "capNoteEn"),
                skill = !string.IsNullOrEmpty(skillAttr) && skillAttr != "-"
                    ? skillAttr
                    : (skillList.Count > 0 ? skillList[0] : null),
                skills = skillList,
                statDefName = statAttr,
                mode = Attr(node, "mode"),
                delivery = Attr(node, "delivery"),
                prerequisite = Attr(node, "prerequisite"),
                capstone = string.Equals(Attr(node, "capstone"), "true", StringComparison.OrdinalIgnoreCase),
                costInverseEffectScale = string.Equals(Attr(node, "costInverseEffectScale"), "true", StringComparison.OrdinalIgnoreCase),
                stageField = Attr(node, "stageField"),
                mechApplies = !string.Equals(Attr(node, "mechApplies"), "false", StringComparison.OrdinalIgnoreCase),
                targetStats = ParseStats(statsAttr, statAttr),
                clampMin = ParseFloatOpt(Attr(node, "clampMin")),
                clampMax = ParseFloatOpt(Attr(node, "clampMax")),
                dangerous = ParseDangerous(node, defName)
            };
        }

        /// <summary>
        /// Parses the optional &lt;dangerous&gt; child element. Returns null when absent — and also when
        /// malformed (no label/description, non-positive cost), so a half-authored dangerous tech
        /// degrades to "no dangerous tech" instead of generating a broken def; Tests/Verify.cs
        /// asserts the authored set, so a silently dropped entry fails loudly there.
        ///
        /// A nested tech inherits its identity from the parent chain: the defName the generator
        /// writes and the single prerequisite that gates it. Filling both here means every
        /// consumer (generator, visibility filter, self-check) reads one shape for both kinds.
        /// </summary>
        private static DangerousTechInfo ParseDangerous(XmlNode node, string parentDefName)
        {
            XmlNode child = node["dangerous"];
            if (child == null) return null;

            string label = ChildText(child, "label");
            string description = ChildText(child, "description");
            float cost = ParseFloat(Attr(child, "cost"), 0f);
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(description) || cost <= 0f)
            {
                return null;
            }

            var info = new DangerousTechInfo
            {
                label = label,
                description = description,
                cost = cost,
                defName = string.IsNullOrEmpty(parentDefName)
                    ? null
                    : parentDefName + ResearchLoopsIds.DangerousSuffix,
                prerequisites = new List<string>(),
                standalone = false
            };
            if (!string.IsNullOrEmpty(parentDefName)) info.prerequisites.Add(parentDefName);
            return info;
        }

        /// <summary>
        /// Splits the 'stats' attribute (comma-separated) into the mount list, falling back to
        /// the single 'stat'. A "-" stat (patch-delivered loops) yields no mounts, so those
        /// loops never reach a StatDef.
        /// </summary>
        private static List<string> ParseStats(string statsAttr, string statAttr)
        {
            var result = new List<string>();

            if (!string.IsNullOrEmpty(statsAttr))
            {
                string[] parts = statsAttr.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string name = parts[i].Trim();
                    if (name.Length > 0 && name != "-") result.Add(name);
                }
                return result;
            }

            if (!string.IsNullOrEmpty(statAttr) && statAttr != "-") result.Add(statAttr);
            return result;
        }

        /// <summary>
        /// Splits the 'skills' attribute (comma-separated) into the level-up list, falling back to
        /// the single 'skill'. Always returns a list (never null) so callers can iterate freely; a
        /// stat/patch loop simply gets an empty one.
        /// </summary>
        private static List<string> ParseSkills(string skillsAttr, string skillAttr)
        {
            var result = new List<string>();

            if (!string.IsNullOrEmpty(skillsAttr))
            {
                string[] parts = skillsAttr.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string name = parts[i].Trim();
                    if (name.Length > 0 && name != "-" && !result.Contains(name)) result.Add(name);
                }
            }

            if (!string.IsNullOrEmpty(skillAttr) && skillAttr != "-" && !result.Contains(skillAttr))
            {
                result.Insert(0, skillAttr);
            }
            return result;
        }

        /// <summary>Parses an optional float attribute; absent or unparseable yields null (no clamp).</summary>
        private static float? ParseFloatOpt(string text)
        {
            float value;
            if (!string.IsNullOrEmpty(text) &&
                float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value))
                return value;
            return null;
        }

        private static string Attr(XmlNode node, string name)
        {
            XmlAttribute attr = node.Attributes != null ? node.Attributes[name] : null;
            return attr != null ? attr.Value : null;
        }

        private static string ChildText(XmlNode node, string name)
        {
            XmlNode child = node[name];
            return child != null ? child.InnerText : null;
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            if (!string.IsNullOrEmpty(text) && int.TryParse(text.Trim(), out value)) return value;
            return fallback;
        }

        private static float ParseFloat(string text, float fallback)
        {
            float value;
            if (!string.IsNullOrEmpty(text) &&
                float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.IsNaN(value) && !float.IsInfinity(value))
                return value;
            // Note: float.TryParse happily accepts "NaN"/"Infinity", so the finite check is not
            // redundant — an authored NaN here would otherwise poison every downstream loop.
            return fallback;
        }
    }
}
