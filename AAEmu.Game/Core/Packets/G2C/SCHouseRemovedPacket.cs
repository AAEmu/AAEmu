using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Removes the timeline id from housing-list containers without removing the spatial house unit.
/// </remarks>
public class SCHouseRemovedPacket(ushort tl) : GamePacket(SCOffsets.SCHouseRemovedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)tl);
        return stream;
    }
}
