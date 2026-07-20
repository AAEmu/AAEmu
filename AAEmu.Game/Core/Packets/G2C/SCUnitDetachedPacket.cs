using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitDetachedPacket(uint objId, AttachUnitReason reason) : GamePacket(SCOffsets.SCUnitDetachedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((byte)reason);
        return stream;
    }
}
