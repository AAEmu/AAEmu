using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZNpcStartDespawn (0x004) — World → Zone: tell NPC AI GO_TO_DESPAWN.
/// After corpse loot timeout World should send this so Zone removes the NPC and NpcSpawner can respawn
/// (ZWRemoveNpc → later ZWSpawnNpc).
/// </summary>
public class WZNpcStartDespawnPacket(uint bcId) : ZonePacket(WzOpcodes.NpcStartDespawn)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(bcId);
    }
}
