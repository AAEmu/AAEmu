using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells a member that the ensemble is ready to play, naming the leader whose instrument leads it.
/// </summary>
public class SCStartToPerformAnEnsemblePacket(uint maestroBc) : GamePacket(SCOffsets.SCStartToPerformAnEnsemblePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(maestroBc);
        return stream;
    }
}
