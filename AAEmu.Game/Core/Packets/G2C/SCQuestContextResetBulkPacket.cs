using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x192 — batch of quest context ids to clear from the client completed list.
/// Body: u8 count, then count × u32 context id.
/// </summary>
public class SCQuestContextResetBulkPacket(IReadOnlyList<uint> questIds)
    : GamePacket(SCOffsets.SCQuestContextResetBulkPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(questIds.Count, 255);
        stream.Write((byte)count);
        for (var i = 0; i < count; i++)
            stream.Write(questIds[i]);
        return stream;
    }
}
