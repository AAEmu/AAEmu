namespace AAEmu.Game.Models.Game.Units
{
    public enum UnitTypeFlag
    {
        None = 0,
        Character = 1,
        Npc = 2,
        Slave = 4,
        Housing = 8,
        Transfer = 16,
        Mate = 32,
        Shipyard = 64
        //128 Unused?
    }
}
