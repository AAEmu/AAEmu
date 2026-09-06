using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

internal class FarmGroupDoodads
{
    public uint Id { get; init; }
    public FarmType FarmId { get; init; }
    public uint DoodadId { get; init; }
    public uint ItemId { get; set; }
}
