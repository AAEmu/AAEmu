using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpertLimitModifiedPacket(bool isUpgrade, uint id, int point, byte step)
    : GamePacket(SCOffsets.SCExpertLimitModifiedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // isUpgrade then the same pisc(id, point)+step entry as SCActability / labor-changed.
        // A bare u32 id + step leaves the client without point and shifts the step byte.
        stream.Write(isUpgrade);
        stream.WritePisc(id, (uint)Math.Max(0, point));
        stream.Write(step);
        return stream;
    }
}
