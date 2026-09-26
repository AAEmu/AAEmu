using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.SailingActivity;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The client's answer to a sailing-activity reward claim: a signed 32-bit <c>activityId</c>
/// followed by three element containers.
/// </summary>
/// <remarks>
/// All three containers share one unrecovered element layout, so they are carried verbatim. A
/// server cannot author them from decoded elements, so this packet is only constructible from
/// captured bytes. Nothing constructs it yet.
/// </remarks>
public class SCSailingActivityClaimRewardResponsePacket(
    int activityId,
    SailingActivityContainer first,
    SailingActivityContainer second,
    SailingActivityContainer third)
    : GamePacket(SCOffsets.SCSailingActivityClaimRewardResponsePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(activityId);
        (first ?? SailingActivityContainer.Empty).Write(stream);
        (second ?? SailingActivityContainer.Empty).Write(stream);
        (third ?? SailingActivityContainer.Empty).Write(stream);
        return stream;
    }
}
