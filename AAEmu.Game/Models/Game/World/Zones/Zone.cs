using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.World.Zones;

public class Zone
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint ZoneKey { get; set; }
    public uint GroupId { get; set; }
    public bool Closed { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint ZoneClimateId { get; set; }
}
