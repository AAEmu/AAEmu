using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.SailingActivity;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client asking to claim one sailing-activity reward.
/// </summary>
/// <remarks>
/// The 10.0.2.13 serializer reads a signed 32-bit <c>activityId</c> and then one element
/// container. The container is decoded as a counted vector of 32-bit ids; nothing acts on this
/// packet yet.
/// </remarks>
public class CSSailingActivityClaimRewardPacket() : GamePacket(CSOffsets.CSSailingActivityClaimRewardPacket, 1)
{
    public int ActivityId { get; private set; }

    /// <summary>
    /// The trailing element container: a counted vector of 32-bit ids.
    /// </summary>
    public SailingActivityContainer Container { get; private set; } = SailingActivityContainer.Empty;

    public override void Read(PacketStream stream)
    {
        ActivityId = stream.ReadInt32();
        Container = SailingActivityContainer.Read(stream, nameof(CSSailingActivityClaimRewardPacket));
    }
}
