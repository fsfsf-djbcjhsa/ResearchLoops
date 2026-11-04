using RimWorld;
using Verse;

namespace LocalMods.ResearchLoops
{
    /// <summary>
    /// Single owner of "which pawns a research loop may affect" (DESIGN §13.1: only the
    /// player's own free colonists and slaves; prisoners, guests, caravans, mechoids, turrets
    /// and animals are excluded).
    ///
    /// This rule is consulted from every independent call site — the buff hedgeiff sweep
    /// (ResearchLoopBuff), the gene sweep (ResearchLoopGene) and the Projectile.DamageAmount /
    /// Need_Food / item-quality patches — so it lives here rather than being copied into each.
    /// The predecessor duplicated it verbatim in two files, which meant a future rule change
    /// (e.g. also allowing prisoners) could be applied to one copy and silently missed in the
    /// other.
    ///
    /// ONE exception, added 2026-10-22 with 合成进化: <see cref="IsLoopTarget"/> also admits the
    /// colony's own mechs. It is a separate predicate on purpose — see its own doc.
    /// </summary>
    public static class LoopEligibility
    {
        /// <summary>
        /// True when the pawn is one this mod is allowed to buff. Null, dead and destroyed
        /// pawns are always false so callers can pass an unresolved lookup straight in.
        /// </summary>
        public static bool IsSupported(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return false;
            return pawn.IsFreeColonist || pawn.IsSlave;
        }

        /// <summary>
        /// True for the colony's own mechanoids. <c>Pawn.IsColonyMech</c> already encodes the
        /// Biotech-active + player faction + not-mental-broken conditions, so enemy and feral
        /// mechs never qualify.
        /// </summary>
        public static bool IsColonyMech(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return false;
            return pawn.IsColonyMech;
        }

        /// <summary>
        /// True when a research loop may be delivered to this pawn: the normal colonist/slave rule,
        /// PLUS colony mechs once 合成进化 has been researched (user directive, 2026-10-22 —
        /// 「机械体也能吃到循环科技加成」).
        ///
        /// This is deliberately a SECOND predicate rather than a wider <see cref="IsSupported"/>:
        /// mechs must never reach the gene sweep (they cannot carry genes), the skill patches
        /// (no skills) or the hunger patch (no food need), and widening the shared rule would send
        /// them there. Only the buff sweep and the three patches that make sense for a mech ask
        /// this question.
        /// </summary>
        public static bool IsLoopTarget(Pawn pawn)
        {
            if (IsSupported(pawn)) return true;
            if (!DangerousTech.SyntheticAscended) return false;
            return IsColonyMech(pawn);
        }

        /// <summary>
        /// True when a research loop may be delivered to a turret building: a player-owned
        /// <c>Building_Turret</c> (covers <c>Building_TurretGun</c> and
        /// <c>Building_TurretMortar</c>). Turrets are not pawns, so they cannot carry the loop
        /// Hediff — only the two patch-delivered ranged loops (RL_RangedDamage,
        /// RL_RangedAccuracy) reach them, through their launcher / caster being one of these.
        /// Added 2026-10-23 (user directive: 「炮台也能吃到加成」).
        /// </summary>
        public static bool IsPlayerTurret(Thing thing)
        {
            if (thing == null) return false;
            if (thing.Faction == null || !thing.Faction.IsPlayer) return false;
            return thing is Building_Turret;
        }
    }
}
