using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.SailingActivity;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client that a sailing-activity stage became available.
/// </summary>
/// <remarks>
/// The 10.0.2.13 serializer writes a signed 32-bit <c>activityId</c> then one element container.
/// The container's element layout is unrecovered, so it is emitted verbatim; a server cannot
/// author one from decoded elements. Nothing constructs this packet yet.
/// </remarks>
public class SCSailingActivityStageUnlockedPacket(int activityId, SailingActivityContainer container)
    : GamePacket(SCOffsets.SCSailingActivityStageUnlockedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(activityId);
        (container ?? SailingActivityContainer.Empty).Write(stream);
        return stream;
    }
}
