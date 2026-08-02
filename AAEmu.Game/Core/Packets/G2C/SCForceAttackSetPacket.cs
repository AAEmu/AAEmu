using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: bc, bool on, bool forced.
/// 1.2 stopped after "on", leaving the client to take the next packet's first byte as "forced".
/// </remarks>
public class SCForceAttackSetPacket(uint objId, bool on, bool forced = false) : GamePacket(SCOffsets.SCForceAttackSetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(on);
        stream.Write(forced);
        return stream;
    }
}
