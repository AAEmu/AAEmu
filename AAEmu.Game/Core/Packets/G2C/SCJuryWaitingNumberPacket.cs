using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Replies the queue number to the player
/// </summary>
/// <param name="waitingNumber"></param>
public class SCJuryWaitingNumberPacket(int waitingNumber) : GamePacket(SCOffsets.SCJuryWaitingNumberPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(waitingNumber);
        return stream;
    }
}
