using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Current-native 0x37f: bool isEnd, u8 count (maximum 20), i32 expeditionId, records.</summary>
public sealed class SCExpeditionInstanceHistoryInfoListPacket(
    bool isEnd,
    uint expeditionId,
    IReadOnlyList<ExpeditionInstanceHistory> histories)
    : GamePacket(SCOffsets.SCExpeditionInstanceHistoryInfoListPacket, 1)
{
    public const int MaximumHistories = 20;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(MaximumHistories, histories?.Count ?? 0);
        stream.Write(isEnd).Write((byte)count).Write(expeditionId);
        for (var index = 0; index < count; index++)
            stream.Write(histories[index]);
        return stream;
    }
}
