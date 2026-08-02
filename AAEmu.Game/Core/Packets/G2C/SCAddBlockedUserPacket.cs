using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCAddBlockedUserPacket(Blocked blocked, bool success, ErrorMessageType errorMessage)
    : GamePacket(SCOffsets.SCAddBlockedUserPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(blocked);
        stream.Write(success);
        stream.Write((short)errorMessage);
        return stream;
    }
}
