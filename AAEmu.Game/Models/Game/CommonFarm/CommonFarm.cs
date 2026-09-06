using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

internal class CommonFarm
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public FarmType FarmId { get; set; }
    public uint GuardTime { get; set; }
    public string Comments { get; set; }
}
