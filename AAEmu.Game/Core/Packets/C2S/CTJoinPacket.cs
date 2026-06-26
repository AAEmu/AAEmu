using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTJoinPacket() : StreamPacket(CTOffsets.CTJoinPacket)
{
    public override void Read(PacketStream stream)
    {
        // Client CTJoin serializer: accountId(u64), cookie(u32), immigrationHash,
        // passportKey(u64), passportPass(u64). accountId is u64 — reading it as u32 made cookie pick up the
        // high dword (0) → StreamManager refused the join. Same fix as X2EnterWorld.
        var accountId = (uint)stream.ReadUInt64();
        var cookie = stream.ReadUInt32();

        StreamManager.Instance.Login(Connection, accountId, cookie);
    }
}
