namespace AAEmu.Game.Models.Game.Units.Static
{
    // TODO: Verify these values
    public enum UnitDetachReason : byte
    {
        None = 0x00,
        Unmount = 0x01,
        Death = 0x05,
        Unsummon = 0x0A,
        LeaveWorld = 0x0F,
    }
}
