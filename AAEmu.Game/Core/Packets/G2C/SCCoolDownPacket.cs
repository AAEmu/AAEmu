using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCooldownsPacket(UnitCooldowns cooldowns) : GamePacket(SCOffsets.SCCooldownsPacket, 1)
{
    // The wire reserves a fixed 150-entry capacity for each bucket.
    private const int MaximumEntriesPerBucket = 150;

    public override PacketStream Write(PacketStream stream)
    {
        var skillEntries = cooldowns.GetActiveSnapshots(MaximumEntriesPerBucket);

        stream.Write((uint)skillEntries.Count);
        foreach (var entry in skillEntries)
        {
            stream.Write(entry.SkillId);
            stream.Write(entry.Duration);
            stream.Write(entry.Remaining);
        }

        // The protocol requires both counts even when those buckets are empty.
        stream.Write(0u); // tagCount
        stream.Write(0u); // chargeCount

        return stream;
    }
}
