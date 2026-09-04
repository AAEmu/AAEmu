using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Squad member connect state: u64 worldCharKey, bool offline.
/// </summary>
public class SCConnectStateMemberPacket(long worldCharKey, bool offline) : GamePacket(SCOffsets.SCConnectStateMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(offline);
        return stream;
    }
}
