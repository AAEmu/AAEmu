namespace AAEmu.Game.Models.Game.Expeditions;

internal readonly record struct GuildLoginAssociationDecision(uint ExpeditionId, bool HealCharacterRow);

/// <summary>
/// Resolves which guild a character should associate with at login when
/// <c>characters.expedition_id</c> and <c>expedition_members</c> disagree.
/// Prestige-shop buff bonuses (and every other guild login path) need the
/// character's Expedition set; a zero character row with a live roster row is a
/// durable desync that otherwise skips ApplyBuffBonuses forever.
/// </summary>
internal static class GuildLoginAssociationRules
{
    /// <summary>
    /// A non-zero character row wins. When it is zero, a positive roster expedition id
    /// is authoritative and the character row must be healed before Save can rewrite zero.
    /// </summary>
    public static GuildLoginAssociationDecision Resolve(uint characterExpeditionId, uint? rosterExpeditionId)
    {
        if (characterExpeditionId != 0)
            return new GuildLoginAssociationDecision(characterExpeditionId, HealCharacterRow: false);
        if (rosterExpeditionId is > 0)
            return new GuildLoginAssociationDecision(rosterExpeditionId.Value, HealCharacterRow: true);
        return new GuildLoginAssociationDecision(0, HealCharacterRow: false);
    }
}
