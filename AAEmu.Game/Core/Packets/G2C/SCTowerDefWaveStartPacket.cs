using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Layout per the 10.0.2.13 client's serializer: TowerDefKey supplies type (i32) and
/// type (i16), then eventZoneId (u32), step (u32) and isSyncStep.
/// </remarks>
public class SCTowerDefWaveStartPacket(
    TowerDefKey key, uint eventZoneId, uint step, bool isSyncStep = false)
    : GamePacket(SCOffsets.SCTowerDefWaveStartPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(key);
        stream.Write(eventZoneId);
        stream.Write(step);
        stream.Write(isSyncStep);
        return stream;
    }
}
