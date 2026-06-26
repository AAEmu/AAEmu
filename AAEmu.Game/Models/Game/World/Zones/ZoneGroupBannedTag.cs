namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneGroupBannedTag
{
    public uint Id { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint TagId { get; set; }
    public uint BannedPeriods { get; set; } // 10.0.2.13: banned_periods_id renamed to banned_periods
}