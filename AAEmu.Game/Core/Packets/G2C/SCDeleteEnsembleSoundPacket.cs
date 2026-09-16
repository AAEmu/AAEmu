using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells a player to drop an ensemble part it is holding, naming whose part it was.
/// </summary>
public class SCDeleteEnsembleSoundPacket(uint bc) : GamePacket(SCOffsets.SCDeleteEnsembleSoundPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        return stream;
    }
}
