using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCheckRaceCongestionPacket() : GamePacket(CSOffsets.CSCheckRaceCongestionPacket, 1)
{
    // 10.0.2.13 (binary CSCheckRaceCongestion 0x39c42e10): a "id" group (no presence byte on the wire)
    // wrapping a single i64. Sent during enter-world, before SpawnCharacter.
    public override void Read(PacketStream stream)
    {
        _ = stream.ReadInt64(); // "type" id (TODO(v10): identify; observed 3) — not needed to answer the check
        // RUNTIME-VERIFIED: the response "result" byte = 1 (canEnter) lets the client proceed; result = 0 shows the
        // "cannot enter the world with this character" congestion dialog. So send true.
        Connection.SendPacket(new SCCheckRaceCongestionResponsePacket(true));
    }
}
