using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

internal class CommonFarm
{
    public uint Id { get; init; }
    // public string Name { get; set; }
    public FarmGroupKind FarmId { get; init; }
    public uint GuardTime { get; init; }
    public string Comments { get; init; }
}
