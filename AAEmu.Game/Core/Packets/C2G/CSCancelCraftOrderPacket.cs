using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Withdraws one of the character's own craft orders and returns the fee it was holding.
/// </summary>
public class CSCancelCraftOrderPacket() : GamePacket(CSOffsets.CSCancelCraftOrderPacket, 1)
{
    public ulong EntryId { get; private set; }

    public override void Read(PacketStream stream)
    {
        EntryId = stream.ReadUInt64();

        if (Connection?.ActiveChar is { } character)
            CraftOrderManager.Instance.Cancel(character, EntryId);
    }
}
