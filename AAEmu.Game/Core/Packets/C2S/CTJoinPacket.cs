using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTJoinPacket() : StreamPacket(CTOffsets.CTJoinPacket)
{
    public override void Read(PacketStream stream)
    {
        // passportKey(i64), passportPass(i64). accountId is i64 — reading it as u32 made cookie pick up the
        // high dword (0) → StreamManager refused the join. Same fix as X2EnterWorld.
        var accountId = stream.ReadInt64();
        var cookie = stream.ReadUInt32();
        _ = stream.ReadString(); // immigrationHash; local stream authentication uses the game-session cookie
        _ = stream.ReadInt64(); // passportKey
        _ = stream.ReadInt64(); // passportPass

        StreamManager.Instance.Login(Connection, accountId, cookie);
    }
}
