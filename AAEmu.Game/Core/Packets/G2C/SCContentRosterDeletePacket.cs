using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Definitive answer to a content-roster removal (10.0.2.13 <c>SCContentRosterDeletePacket</c>).
/// </summary>
/// <remarks>
/// 10.0.2.13 client serializer field order:
/// u8 result, u8 isExpired, u16 ErrorMessage
/// </remarks>
public class SCContentRosterDeletePacket(bool result, bool isExpired, ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCContentRosterDeletePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write(isExpired);
        stream.Write((ushort)errorMessage);
        return stream;
    }
}
