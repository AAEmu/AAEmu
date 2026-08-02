using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i8 (unnamed), i8 portalType, u32 id.
/// 1.2 opened at portalType, so every field the client read was one byte early.
/// </remarks>
public class SCPortalDeletedPacket(byte portalType, int portalId, sbyte unnamed1 = 0) : GamePacket(SCOffsets.SCPortalDeletedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unnamed1);
        stream.Write(portalType);
        stream.Write(portalId);
        return stream;
    }
}
