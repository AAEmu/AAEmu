using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 reads a single i32 the serializer names "type" — the tutorial id. The 1.2 trailing body is
/// not read and desynced everything the client parsed after it.
/// </remarks>
public class SCTutorialSavedPacket(uint id) : GamePacket(SCOffsets.SCTutorialSavedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)id);
        return stream;
    }
}
