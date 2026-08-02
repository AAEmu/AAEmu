using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// chargeCount, each entry three u32 — then creatorName[128], u64(owner) and dbId s32.
/// </summary>
public class SCSlaveStatePacket(uint objId, ushort tlId, string creatorName, uint ownerId, uint dbId)
    : GamePacket(SCOffsets.SCSlaveStatePacket, 1)
{
    private readonly int _skillCount = 0;
    private readonly int _tagCount = 0;
    private readonly int _chargeCount = 0;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(tlId);
        stream.Write(0ul); // type
        stream.Write(_skillCount);
        stream.Write(_tagCount);
        stream.Write(_chargeCount);
        stream.Write(creatorName ?? string.Empty); // a slave with no summoner has no creator name
        stream.Write((ulong)ownerId);
        stream.Write(dbId);
        return stream;
    }
}
