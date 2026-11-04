namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Shared identifiers. Kept in its own file so consumers that must compile without the mod's
    /// Verse-dependent entry point (the verification project) can still reference the packageId.
    /// </summary>
    public static class ResearchLoopsIds
    {
        /// <summary>
        /// Harmony id and mod packageId. MUST equal the packageId in About/About.xml - not as
        /// bookkeeping, but because <see cref="ResearchLoopTemplates"/> resolves this mod's folder
        /// by it, and the def generator then finds Data/ResearchLoopTemplates.xml or finds nothing
        /// (2026-10-31 user report "游戏里没有研究选项": §36 renamed the About.xml id to
        /// liaoqingshan.researchloops for the workshop release and left this constant behind, so
        /// every loop def silently failed to generate and the page came up empty).
        /// Tests/Verify.cs now cross-checks the two, so a rename cannot pass the build again.
        /// </summary>
        public const string PackageId = "liaoqingshan.researchloops";

        /// <summary>Harmony id used for patching.</summary>
        public const string HarmonyId = PackageId;

        /// <summary>
        /// defName of this mod's own research page (Defs/ResearchTabDefs/ResearchLoopsTab.xml).
        /// The def generator writes it into every loop def's &lt;tab&gt;, so the two must match:
        /// Tests/Verify.cs cross-checks this constant against the authored XML.
        /// </summary>
        public const string TabDefName = "ResearchLoops";

        /// <summary>
        /// defName of the vanilla Hi-Tech Research Bench (Core/Buildings_Production.xml). Used by the
        /// optional "require a Hi-Tech Research Bench" gate: the def generator writes it into every
        /// loop def's &lt;requiredResearchBuilding&gt; when the setting is on. Pinned here (and cross-checked
        /// against the game in Tests/Verify.cs) so a renamed vanilla building fails loudly.
        /// </summary>
        public const string HiTechBenchDefName = "HiTechResearchBench";

        /// <summary>
        /// Suffix that turns a loop's defName into its dangerous-tech defName
        /// ("RL_RestFall" -> "RL_RestFall_Danger"). The dangerous tech is a ONE-SHOT project that
        /// appears once the parent chain is complete and drives that chain's coefficient to zero
        /// (user directive, 2026-10-22). Pinned here because the generator, the runtime lookup and
        /// the verifier must all derive the same name.
        /// </summary>
        public const string DangerousSuffix = "_Danger";

        /// <summary>
        /// How far above the parent's node a dangerous tech is parked (in page coordinates) while
        /// the chain is incomplete — and where the completed parent is parked after the handover.
        /// Derived from the parent's coordinate so the three dangerous techs can never share a
        /// spot; 0.1 below a node is always free because the page grid steps 1.2 in Y from 1.
        /// Tests/Verify.cs asserts no loop occupies any dangerous tech's parking spot.
        /// </summary>
        public const float DangerousParkOffsetY = 0.1f;
    }
}
