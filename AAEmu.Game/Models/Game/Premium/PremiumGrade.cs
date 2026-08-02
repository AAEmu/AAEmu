namespace AAEmu.Game.Models.Game.Premium;

/// <summary>
/// Row of <c>premium_grades</c>. A character's grade is the highest one whose <see cref="Point"/>
/// threshold their premium point total reaches.
/// </summary>
public class PremiumGrade
{
    public uint Id { get; set; }
    public uint GradeId { get; set; }
    public int Point { get; set; }
    public uint BuffId { get; set; }
    public int OnlineLabor { get; set; }
    public int OfflineLabor { get; set; }
    public int MaxLabor { get; set; }
    public int MaxLocalLabor { get; set; }
    public uint ConfigId { get; set; }
}

/// <summary>
/// Row of <c>premium_configs</c>: the point economy the grades are measured against.
/// </summary>
public class PremiumConfig
{
    public uint Id { get; set; }
    public int ConnectPoint { get; set; }
    public int DisconnectPoint { get; set; }
    public int DeactivatePoint { get; set; }
    public int MaxPoint { get; set; }
    public int AuctionChargeDiscount { get; set; }
    public int AuctionDepositDiscount { get; set; }
    public uint MaxGradeId { get; set; }
}
