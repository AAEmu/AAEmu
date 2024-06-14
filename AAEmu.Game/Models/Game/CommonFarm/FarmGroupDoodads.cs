using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.Game.Models.Game.CommonFarm;

class FarmGroupDoodads
{
    public uint Id { get; set; }
    public FarmType FarmGroupId { get; set; }
    public uint DoodadId { get; set; }
    public uint ItemId { get; set; }
}
