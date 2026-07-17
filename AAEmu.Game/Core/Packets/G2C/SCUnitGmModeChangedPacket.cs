using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitGmModeChangedPacket(uint unitId, int mode, byte value)
    : GamePacket(SCOffsets.SCUnitGmModeChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(mode);
        stream.Write(value);
        return stream;
    }
}
