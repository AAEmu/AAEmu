using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// TODO: Integrate this packet into CurrentTarget property of BaseUnit
public class SCTargetChangedPacket(uint id, uint targetId) : GamePacket(SCOffsets.SCTargetChangedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.WriteBc(targetId);
        return stream;
    }
}
