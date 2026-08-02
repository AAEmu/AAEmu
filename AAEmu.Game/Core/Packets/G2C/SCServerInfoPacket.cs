using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCServerInfoPacket(long serverOpenTime) : GamePacket(SCOffsets.SCServerInfoPacket, 1)
{
    // emits Value("serverOpenTime", obj+16) via the ISerialize u64 slot (vtbl+0x78). Unix seconds (capture:
    public SCServerInfoPacket() : this(Helpers.UnixTimeNow()) { }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(serverOpenTime);
        return stream;
    }
}
