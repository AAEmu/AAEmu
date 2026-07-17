using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitAttachedPacket(uint childUnitObjId, AttachPointKind point, AttachUnitReason reason, uint id)
    : GamePacket(SCOffsets.SCUnitAttachedPacket, 5)
{
    private readonly byte _point = (byte)point;
    private readonly byte _reason = (byte)reason;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(childUnitObjId);

        stream.Write(_point);
        // if (_point != -1) - byte can't be negative anyways
        stream.WriteBc(id);

        stream.Write(_reason);
        return stream;
    }
}
