namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Row of <c>expedition_levels</c>: a guild level's cumulative exp threshold and the perks/limits
/// that come with it. <c>Id</c> doubles as the level number (rows start at id 1, total_exp 0).
/// </summary>
public class ExpeditionLevel
{
    public uint Id { get; set; }
    public long TotalExp { get; set; }
    public long DailyExp { get; set; }
    public int MemberLimit { get; set; }
    public int SummonLimit { get; set; }

    /// <summary>0 when this level is reached automatically once its exp threshold is met.</summary>
    public uint RequireItemId { get; set; }
    public int RequireItemAmount { get; set; }
    public int DailyContributionPoint { get; set; }
    public int PortalPointLimit { get; set; }
}
