using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The player nominated (or cleared) the account's main character in the character select screen.
/// </summary>
/// <remarks>
/// Client layout (serialize at RVA 0xC6B750): u64 "type" - the character id - then bool "isDeleted",
/// which is set when the nomination is being cleared rather than made.
///
/// The flag only ever travels in this direction. The character list entry has no field for it (its
/// last member is "guid", followed by the labor block) and there is no server-to-client packet for
/// it either, so the client keeps its own copy - which is what produces "Must deselect as Main
/// Character before deleting." without the server being involved. We record the choice so the
/// deletion guard in CharacterManager can act on it.
/// </remarks>
public class CSRepresentCharacter() : GamePacket(CSOffsets.CSRepresentCharacter, 1)
{
    public ulong Type { get; private set; }
    public bool IsDeleted { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();     // u64  type
        IsDeleted = stream.ReadBoolean(); // bool isDeleted

        CharacterManager.Instance.SetRepresentCharacter(Connection, (uint)Type, IsDeleted);
    }
}
