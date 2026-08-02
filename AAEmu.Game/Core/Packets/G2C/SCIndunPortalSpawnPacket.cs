using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCIndunPortalSpawnPacket(bool show, uint indunZoneKey, uint portalZoneKey, float portalPosVecX, float portalPosVecY, float portalPosVecZ) : GamePacket(SCOffsets.SCIndunPortalSpawnPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(show);
        stream.Write(indunZoneKey);
        stream.Write(portalZoneKey);
        stream.Write(portalPosVecX);
        stream.Write(portalPosVecY);
        stream.Write(portalPosVecZ);
        return stream;
    }
}
