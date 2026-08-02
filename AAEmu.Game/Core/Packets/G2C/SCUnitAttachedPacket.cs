using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 0xFF is <see cref="AttachPointKind.System"/>, which marks a detached/system attachment.
/// </summary>
public class SCUnitAttachedPacket(uint childUnitObjId, AttachPointKind point, AttachUnitReason reason, uint id)
    : GamePacket(SCOffsets.SCUnitAttachedPacket, 1)
{
    private const byte NoAttachPoint = (byte)AttachPointKind.System;

    private readonly byte _point = (byte)point;
    private readonly byte _reason = (byte)reason;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(childUnitObjId);

        stream.Write(_point);
        if (_point != NoAttachPoint)
            stream.WriteBc(id);

        stream.Write(_reason);
        return stream;
    }
}
