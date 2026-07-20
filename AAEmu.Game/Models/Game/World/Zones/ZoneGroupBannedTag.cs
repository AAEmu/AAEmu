namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneGroupBannedTag
{
    public uint Id { get; set; }

    public string Usage { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint TagId { get; set; }
    public uint BannedPeriodsId { get; set; }
}