using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// success=true is what triggers the client to play slaves.portal_despawn_fx_id on the unit
/// byte as success and skipped the despawn portal.
/// </summary>
public class SCSlaveDespawnPacket(uint id, bool success = true) : GamePacket(SCOffsets.SCSlaveDespawnPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(success);
        return stream;
    }
}
