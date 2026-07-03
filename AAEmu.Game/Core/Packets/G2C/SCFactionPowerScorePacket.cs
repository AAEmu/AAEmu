using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionPowerScorePacket() : GamePacket(SCOffsets.SCFactionPowerScorePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Reference world-entry body is 21 bytes: four per-faction power scores (u32, all 0 at entry), a
        // season/index dword (=1), and a trailing status byte (0).
        // TODO: confirm field grouping against the x2game deserializer for faction power score.
        stream.Write(0u);
        stream.Write(0u);
        stream.Write(0u);
        stream.Write(0u);
        stream.Write(1u);
        stream.Write((byte)0);

        return stream;
    }
}
