using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x319 broadcast: u64 worldCharKey, u8 charLevel.
/// </summary>
public class SCChangeSquadMemberLevelPacket(ulong worldCharKey, byte charLevel)
    : GamePacket(SCOffsets.SCChangeSquadMemberLevelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(charLevel);
        return stream;
    }
}
