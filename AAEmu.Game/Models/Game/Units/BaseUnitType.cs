namespace AAEmu.Game.Models.Game.Units;

public enum BaseUnitType : byte
{
    Character = 0,
    Npc = 1,
    Slave = 2,
    Housing = 3,
    Transfer = 4,
    Mate = 5,
    Shipyard = 6,
    Invalid = 255,
}
