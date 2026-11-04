using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Load-time generator. One job, driven by Data/ResearchLoopTemplates.xml:
    ///   append ONE ResearchProjectDef per authored loop (37 total), plus one one-shot
    ///   dangerous-tech def per loop that authors one, plus one def per standalone
    ///   &lt;Dangerous&gt; capstone (心胜于物).
    ///
    /// It runs while RimWorld applies XML patches to the unified def document, i.e. BEFORE
    /// defs are parsed (verified order in LoadedModManager.LoadAllActiveMods:
    /// LoadModXML -> CombineIntoUnifiedXML -> ApplyPatches -> ParseAndProcessXML). Nodes
    /// appended here become ordinary Defs and are added to DefDatabase by
    /// DefDatabase&lt;T&gt;.AddAllInMods from LoadedModManager.PatchedDefsForReading.
    ///
    /// HARD RULE: never resolve a def in here. DefDatabase is empty at this point. The
    /// predecessor looked each template up with GetNamedSilentFail and therefore generated
    /// zero level defs on every load, which is exactly the reported "research finishes and
    /// there is no next level" defect. Everything needed is read from
    /// Data/ResearchLoopTemplates.xml (mod folder) and ResearchLoopPlan (pure).
    ///
    /// One def per chain, not one per level: the level is a counter in
    /// GameComponent_ResearchLoops, projected onto this def by ResearchLoopProgress.
    /// A def-per-level design needs ~1710 defs that all share one researchView coordinate,
    /// which ResearchProjectDef.ConfigErrors() reports pairwise (O(N^2) config errors) and
    /// which GenerateNonOverlappingCoordinates() then tries to pull apart at startup.
    ///
    /// Delivery channels are NOT generated here (§15): skill loops live in the gene, patch
    /// loops in their Harmony patches, and the 23 stat loops ride ONE buff hediff
    /// (Hediff_ResearchLoop) that reads the same templates at runtime. This method only counts
    /// them for the load log, so a channel that silently lost every loop shows up as a line in
    /// Player.log instead of a stat that never moves.
    /// </summary>
    public class PatchOperation_GenerateLoopDefs : PatchOperation
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        protected override bool ApplyWorker(XmlDocument xml)
        {
            // Defs are generated once per launch, so this is the one place where the language must
            // be decided for every generated label/description (2026-10-23 bilingual support).
            Localization.Sync();

            List<ResearchLoopTemplate> templates = ResearchLoopTemplates.All;
            if (templates.Count == 0)
            {
                Log.Error("[ResearchLoops] no loop templates loaded; nothing generated.");
                return false;
            }

            XmlElement defsRoot = xml.DocumentElement;
            if (defsRoot == null)
            {
                Log.Error("[ResearchLoops] patch target document has no root element.");
                return false;
            }

            foreach (ResearchLoopTemplate template in templates)
            {
                defsRoot.AppendChild(BuildDef(xml, template));
                // The loop's dangerous tech (when authored): a second, ONE-SHOT def that unlocks
                // once the chain above is complete and zeroes its coefficient. It carries no loop
                // marker — finishing it must not touch the parent's counter (Patch_FinishProject
                // re-projects the parent instead).
                if (template.dangerous != null)
                {
                    defsRoot.AppendChild(BuildDangerousDef(xml, template));
                }
            }

            // Standalone dangerous techs (心胜于物): capstones shared by several chains. Emitted
            // after the loops so the def order in the patch document reads like the page.
            foreach (DangerousTechInfo info in ResearchLoopTemplates.DangerousTechs)
            {
                defsRoot.AppendChild(BuildStandaloneDangerousDef(xml, info));
            }

            // Delivery-channel census for the load log. Nothing here changes the defs; it only
            // makes an empty channel loud. The stat loops ride ONE buff hediff
            // (Hediff_ResearchLoop) at runtime, so the "mounts" this mod used to write into
            // vanilla StatDef.parts no longer exist (§15).
            int buffLoops = 0, buffStats = 0, patchLoops = 0, skillLoops = 0, qualityLoops = 0, passionLoops = 0, dangerousTechs = 0;
            foreach (ResearchLoopTemplate template in templates)
            {
                if (template.delivery == "buff")
                {
                    buffLoops++;
                    buffStats += template.targetStats == null ? 0 : template.targetStats.Count;
                }
                else if (template.delivery == "patch") patchLoops++;
                else if (template.delivery == "skill") skillLoops++;
                else if (template.delivery == "quality") qualityLoops++;
                else if (template.delivery == "passion") passionLoops++;

                if (template.dangerous != null) dangerousTechs++;
            }
            dangerousTechs += ResearchLoopTemplates.DangerousTechs.Count;

            if (buffLoops == 0 || buffStats == 0)
            {
                Log.Error("[ResearchLoops] no buff-delivered stat loop carries a StatDef; every " +
                          "stat bonus would be silently missing. Expected the authored loops in " +
                          "Data/ResearchLoopTemplates.xml to declare stat/stats.");
            }

            Log.Message("[ResearchLoops] generated " + templates.Count + " loop research defs in tab " +
                        ResearchLoopsIds.TabDefName + "; buff hediff carries " + buffStats +
                        " stat(s) across " + buffLoops + " loop(s), plus " + patchLoops +
                        " patch loop(s), " + skillLoops + " skill loop(s), " + qualityLoops +
                        " quality loop(s) and " + passionLoops + " passion loop(s)" +
                        (dangerousTechs > 0 ? ", plus " + dangerousTechs + " dangerous tech(s)" : ""));
            return true;
        }

        /// <summary>
        /// Builds the one-shot dangerous-tech def for a loop: red-titled node, gated on the parent
        /// chain (whose single def reads as finished exactly at maxLevel), authored cost.
        ///
        /// It deliberately carries NO modExtensions: a dangerous tech is not a loop — it has no
        /// counter, no per-level projection and no buff entry of its own. Its only runtime trace
        /// is the zero it applies to the parent loop, which flows through
        /// ResearchLoopPlan.EffectValue(..., dangerousZero: true) keyed on this def being finished.
        /// </summary>
        private static XmlElement BuildDangerousDef(XmlDocument doc, ResearchLoopTemplate parent)
        {
            DangerousTechInfo info = parent.dangerous;
            XmlElement node = doc.CreateElement("ResearchProjectDef");

            Append(doc, node, "defName", DangerousTech.DefNameFor(parent));
            Append(doc, node, "label", ResearchLoopPlan.Pick(info.label, info.labelEn));
            Append(doc, node, "description", ResearchLoopPlan.Pick(info.description, info.descriptionEn));
            Append(doc, node, "baseCost", Number(info.cost * ResearchLoopsConfig.CostScale));

            // Spacer: a dangerous tech sits past the end of a 50-level chain, and techLevel is
            // mandatory (ConfigErrors reports "techLevel is Undefined" otherwise).
            Append(doc, node, "techLevel", "Spacer");

            Append(doc, node, "tab", ResearchLoopsIds.TabDefName);

            // The gate: the parent loop def, which reads as finished exactly when the chain is
            // complete. The tree line also makes the dependency legible once the node appears.
            XmlElement prerequisites = doc.CreateElement("prerequisites");
            prerequisites.AppendChild(Text(doc, "li", parent.defName));
            node.AppendChild(prerequisites);

            if (ResearchLoopsConfig.RequireHiTechBench)
            {
                Append(doc, node, "requiredResearchBuilding", ResearchLoopsIds.HiTechBenchDefName);
            }

            // Parked just above the parent until the chain completes: the runtime handover
            // (DangerousTech.RefreshPlacement) swaps the two defs, which is how the dangerous node
            // ends up on the chain's own coordinate without two defs ever sharing one (a vanilla
            // config error). The parked spot is derived from the parent's coordinate, so the three
            // dangerous techs cannot collide, and it is always filtered out of the drawn list.
            Append(doc, node, "researchViewX", Number(DangerousTech.ParkX(parent)));
            Append(doc, node, "researchViewY", Number(DangerousTech.ParkY(parent)));

            return node;
        }

        /// <summary>
        /// The research node's text: the authored blurb, then one line per authored effect
        /// (2026-10-23 user directive 「四种飞升效果也写在研究界面上」).
        ///
        /// Those effect lines used to live ONLY on the ascension's own hediff, so the research page
        /// showed flavour prose while the health tab showed what the tech actually does — the two
        /// could drift, and a player had to buy the 100k tech to learn what it bought. Appending the
        /// authored <buffLines> keeps one source of truth: the node and the hediff now quote the
        /// same lines, in the same order, in the active language.
        /// </summary>
        private static string ResearchNodeText(DangerousTechInfo info)
        {
            // Implementation lives in ResearchLoopPlan.StandaloneNodeText so this load-time generator
            // and the runtime refresh (ResearchLoopProgress.RefreshDangerousText) share one copy.
            return ResearchLoopPlan.StandaloneNodeText(info, ResearchLoopProgress.PrereqDisplayName);
        }

        /// <summary>
        /// Builds a STANDALONE dangerous tech (top-level &lt;Dangerous&gt;): a capstone shared by
        /// several chains. Unlike the nested kind it owns its page coordinate — there is no parent
        /// node to inherit — and it sits at the head of its row (2026-11-04, user 「飞升要位于最前边」).
        ///
        /// The authored chain list is NOT written to &lt;prerequisites&gt;. Vanilla reads that list as a
        /// rule and colours it by `prerequisite.IsFinished`, and for a loop def IsFinished is stored
        /// progress compared against the baseCost this mod re-projects at every level-up, so "this
        /// chain is finished" is never the statement "this chain has N levels" (the user hit that
        /// difference in game: 「游戏里飞升只要 2 级就能研究」). Putting the list on the def would show
        /// a 满级-coloured panel; fixing the colours means redrawing it, and the user ruled both out
        /// (2026-11-04 「不要动 UI」). The level check stays this mod's own: Patch_CanStartNow opens
        /// the node at `prereqLevel` on every chain, and Patch_PrerequisitesCompleted feeds the same
        /// answer to vanilla's lock reason so the node is never locked without a reason. The node's
        /// own text is what states the requirement.
        /// </summary>
        private static XmlElement BuildStandaloneDangerousDef(XmlDocument doc, DangerousTechInfo info)
        {
            XmlElement node = doc.CreateElement("ResearchProjectDef");

            Append(doc, node, "defName", info.defName);
            Append(doc, node, "label", ResearchLoopPlan.Pick(info.label, info.labelEn));
            Append(doc, node, "description", ResearchNodeText(info));
            Append(doc, node, "baseCost", Number(info.cost * ResearchLoopsConfig.CostScale));
            Append(doc, node, "techLevel", "Spacer");
            Append(doc, node, "tab", ResearchLoopsIds.TabDefName);

            if (ResearchLoopsConfig.RequireHiTechBench)
            {
                Append(doc, node, "requiredResearchBuilding", ResearchLoopsIds.HiTechBenchDefName);
            }

            // Authored: there is no handover, so the node must already sit where it belongs — at the
            // head of its row, left of every chain it asks for. It carries no prerequisites, so
            // vanilla draws no lines to or from it. It is drawn from the first game and greyed until
            // every prerequisite chain has reached info.prereqLevel.
            Append(doc, node, "researchViewX", Number(info.researchViewX));
            Append(doc, node, "researchViewY", Number(info.researchViewY));

            return node;
        }

        /// <summary>
        /// Builds the single def for one loop, in its level-1 state (0 levels completed).
        /// The label/description/baseCost written here are the same values
        /// ResearchLoopProgress.Apply would compute for completedLevels = 0, so a fresh game
        /// and a loaded game with an empty counter agree.
        /// </summary>
        private static XmlElement BuildDef(XmlDocument doc, ResearchLoopTemplate template)
        {
            XmlElement node = doc.CreateElement("ResearchProjectDef");

            Append(doc, node, "defName", template.defName);
            Append(doc, node, "label", ResearchLoopPlan.Label(template, 0));
            Append(doc, node, "description", ResearchLoopPlan.Description(template, 0));
            Append(doc, node, "baseCost", Number(ResearchLoopPlan.CostForLevel(template, 0,
                ResearchLoopsConfig.CostScale, ResearchLoopsConfig.BonusScale)));

            // Required: ConfigErrors() reports "techLevel is Undefined" otherwise. Industrial
            // matches the MultiAnalyzer gate every loop sits behind.
            Append(doc, node, "techLevel", "Industrial");

            // This mod's own research page (Defs/ResearchTabDefs/ResearchLoopsTab.xml). Without
            // it ResolveReferences() would drop every loop into the main tree at coordinates
            // that collide with vanilla techs.
            Append(doc, node, "tab", ResearchLoopsIds.TabDefName);

            if (!string.IsNullOrEmpty(template.prerequisite))
            {
                XmlElement prerequisites = doc.CreateElement("prerequisites");
                prerequisites.AppendChild(Text(doc, "li", template.prerequisite));
                node.AppendChild(prerequisites);
            }

            // Optional load-time gate (settings option): require a Hi-Tech Research Bench to research
            // the loop. This is a def property, so it is baked when the defs are generated at startup
            // and the toggle takes effect after a restart. HiTechResearchBench is the vanilla building.
            if (ResearchLoopsConfig.RequireHiTechBench)
            {
                Append(doc, node, "requiredResearchBuilding", ResearchLoopsIds.HiTechBenchDefName);
            }

            Append(doc, node, "researchViewX", Number(template.researchViewX));
            Append(doc, node, "researchViewY", Number(template.researchViewY));

            node.AppendChild(BuildModExtension(doc, template.defName));
            return node;
        }

        /// <summary>Writes the loop marker the runtime reads back as ResearchLoopExtension.</summary>
        private static XmlElement BuildModExtension(XmlDocument doc, string loop)
        {
            XmlElement extensions = doc.CreateElement("modExtensions");
            XmlElement entry = doc.CreateElement("li");
            entry.SetAttribute("Class", typeof(ResearchLoopExtension).FullName);
            entry.AppendChild(Text(doc, "loop", loop));
            extensions.AppendChild(entry);
            return extensions;
        }

        private static void Append(XmlDocument doc, XmlElement parent, string name, string value)
        {
            parent.AppendChild(Text(doc, name, value));
        }

        private static XmlElement Text(XmlDocument doc, string name, string value)
        {
            XmlElement element = doc.CreateElement(name);
            element.InnerText = value ?? string.Empty;
            return element;
        }

        private static string Number(float value)
        {
            return value.ToString("0.###", Inv);
        }
    }
}
