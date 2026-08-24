namespace AAEmu.Game.Models.Game.Squad;

/// <summary>
/// Composite character key used by every squad packet. The client stores it as one u64 but
/// reads byte 4 back out on its own as the world id, and resolves the member's server-name
/// column from that byte. A key built from the character id alone therefore leaves the
/// column blank, because world id 0 matches no known world.
/// </summary>
public static class SquadWorldCharKey
{
    private const int WorldIdShift = 32;

    public static ulong Make(uint characterId, byte worldId) =>
        characterId | ((ulong)worldId << WorldIdShift);

    public static uint GetCharacterId(ulong key) => (uint)key;

    public static byte GetWorldId(ulong key) => (byte)(key >> WorldIdShift);
}
