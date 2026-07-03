using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCServerInfoPacket(long serverOpenTime) : GamePacket(SCOffsets.SCServerInfoPacket, 1)
{
    // Body: single "serverOpenTime" u64. x2game-dev_dedicate SCServerInfoPacket::Serialize (sub_39545230)
    // emits Value("serverOpenTime", obj+16) via the ISerialize u64 slot (vtbl+0x78). Unix seconds (capture:
    // 0x6A3D5080). Sent in the lobby config burst right after SCInitialConfig.
    public SCServerInfoPacket() : this(Helpers.UnixTimeNow()) { }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(serverOpenTime);
        return stream;
    }
}
