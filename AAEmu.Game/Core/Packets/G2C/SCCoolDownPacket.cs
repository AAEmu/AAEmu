using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCooldownsPacket(UnitCooldowns cooldowns) : GamePacket(SCOffsets.SCCooldownsPacket, 1)
{
    // Native reserves 0x708 bytes per bucket: u32 count followed by 150 12-byte entries.
    private const int MaximumEntriesPerBucket = 150;

    public override PacketStream Write(PacketStream stream)
    {
        var skillEntries = cooldowns.GetActiveSnapshots(MaximumEntriesPerBucket);

        stream.Write((uint)skillEntries.Count);
        foreach (var entry in skillEntries)
        {
            stream.Write(unchecked((int)entry.SkillId));
            stream.Write(unchecked((int)entry.Duration));
            stream.Write(unchecked((int)entry.Remaining));
        }

        // AAEmu currently has no independent tag or charge cooldown stores. The native body still
        // requires both counts when those buckets are empty.
        stream.Write(0u); // tagCount
        stream.Write(0u); // chargeCount

        return stream;
    }
}
