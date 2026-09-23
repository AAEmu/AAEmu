using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answer to a content-roster save.
/// </summary>
/// <remarks>
/// Field order: bool result, u16 ErrorMessage.
/// </remarks>
public class SCContentRosterSavePacket(bool result, ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCContentRosterSavePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write((ushort)errorMessage);
        return stream;
    }
}
