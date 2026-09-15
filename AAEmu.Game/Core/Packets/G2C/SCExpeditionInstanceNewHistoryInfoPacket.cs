using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Current-native 0x380: i32 expeditionId, rating summary, then one history record.</summary>
public sealed class SCExpeditionInstanceNewHistoryInfoPacket(
    uint expeditionId,
    ExpeditionInstanceRating rating,
    ExpeditionInstanceHistory history)
    : GamePacket(SCOffsets.SCExpeditionInstanceNewHistoryInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream) => stream
        .Write(expeditionId)
        .Write(rating)
        .Write(history);
}
