using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Layout per the 10.0.2.13 client's serializer: TowerDefKey supplies type (i32) and
/// type (i16), then eventZoneId (u32), isStartSeamlessWorld and isBroadCastSeamless.
/// </remarks>
public class SCTowerDefStartPacket(
    TowerDefKey key, uint eventZoneId,
    bool isStartSeamlessWorld = false, bool isBroadCastSeamless = false)
    : GamePacket(SCOffsets.SCTowerDefStartPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(key);
        stream.Write(eventZoneId);
        stream.Write(isStartSeamlessWorld);
        stream.Write(isBroadCastSeamless);
        return stream;
    }
}
