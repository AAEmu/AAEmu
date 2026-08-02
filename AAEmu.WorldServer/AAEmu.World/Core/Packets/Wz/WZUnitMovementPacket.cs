using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// </summary>
public class WZUnitMovementPacket(uint bcId, byte[] movementBody) : ZonePacket(WzOpcodes.UnitMovement)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(bcId);
        if (movementBody is { Length: > 0 })
            stream.Write(movementBody, false);
    }
}
