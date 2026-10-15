namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Resolves which expedition's prestige-shop buff list a CSExpeditionBuff view request wants.
/// Recruit-board "Buff Effect" sends the listed guild id in <c>type</c>; own-guild shop sends the
/// viewer's expedition id the same way. <c>type == 0</c> falls back to the viewer's guild.
/// </summary>
public static class ExpeditionBuffViewRules
{
    public static uint ResolveExpeditionId(int requestedType, uint viewerExpeditionId) =>
        requestedType > 0 ? (uint)requestedType : viewerExpeditionId;
}
