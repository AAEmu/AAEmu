using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlayerGameDataPacket() : GamePacket(SCOffsets.SCPlayerGameDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Reference world-entry body is 12 bytes: revision(u32) loginTime(u32, unix seconds) reserved(u32).
        // The value 7 is the client-data revision the reference reports; the trailing dword is zero at entry.
        // TODO: confirm the semantics of the leading revision field against the x2game deserializer.
        stream.Write(7u);
        stream.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        stream.Write(0u);

        return stream;
    }
}
