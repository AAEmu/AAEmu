using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Closes the ruling window. The client hides its ruling dialog on this event, so it is the last
/// packet of a finished trial.
/// </summary>
/// <remarks>Empty body.</remarks>
public class SCRulingClosedPacket() : GamePacket(SCOffsets.SCRulingClosedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
