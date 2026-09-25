using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Expeditions;

internal enum GuildBuffPurchaseRejection
{
    None,
    MissingBuff,
    InactiveBuff,
    MissingGrade,
    WrongBuff,
    OutOfOrder,
    ExpeditionLevelTooLow,
    ResidenceRequired,
    InvalidCost,
    InsufficientContribution
}

internal readonly record struct GuildBuffPurchaseDecision(GuildBuffPurchaseRejection Reason)
{
    public bool IsAllowed => Reason == GuildBuffPurchaseRejection.None;
}

/// <summary>
/// Content-backed eligibility and price checks for one prestige-shop guild-buff grade.
/// This deliberately does not infer a role requirement, a duration, or an expiration: those are
/// separate questions and must be proven by content before they are enforced.
/// </summary>
internal static class GuildBuffPurchaseRules
{
    private const uint NoItemId = 0;
    private const uint NoResidence = 0;

    public static GuildBuffPurchaseDecision Evaluate(
        ExpeditionBuffTemplate buff,
        ExpeditionBuffGrade grade,
        byte currentGrade,
        uint expeditionLevel,
        uint residenceHouseId,
        uint memberContribution)
    {
        if (buff == null)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.MissingBuff);
        if (!buff.Active)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.InactiveBuff);
        if (grade == null)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.MissingGrade);
        if (grade.ExpeditionBuffId != buff.Id)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.WrongBuff);

        var nextGrade = (uint)currentGrade + 1u;
        if (nextGrade > byte.MaxValue || grade.Grade != nextGrade)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.OutOfOrder);

        if (buff.ExpeditionLevelId > expeditionLevel || grade.ExpeditionLevelId > expeditionLevel)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.ExpeditionLevelTooLow);

        if (grade.Housing && residenceHouseId == NoResidence)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.ResidenceRequired);

        if (grade.Contribution < 0 || grade.Count < 0 ||
            (grade.ItemId == NoItemId && grade.Count != 0) ||
            (grade.ItemId != NoItemId && grade.Count <= 0))
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.InvalidCost);

        if (memberContribution < (uint)grade.Contribution)
            return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.InsufficientContribution);

        return new GuildBuffPurchaseDecision(GuildBuffPurchaseRejection.None);
    }
}
