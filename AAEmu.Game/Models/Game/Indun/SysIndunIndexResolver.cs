using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Builds <see cref="Core.Packets.G2C.SCSysIndunIndexPacket"/> payloads for CSRequestSysInstanceIndex.
/// </summary>
internal static class SysIndunIndexResolver
{
    internal readonly record struct Reply(uint ZoneKey, uint InstanceId, uint InstanceIndex);

    internal static Reply Resolve(
        uint requestZoneKey,
        uint catalogInstId,
        IndunZone dungeonZone,
        IReadOnlyList<uint> zoneKeysInGroup,
        IEnumerable<WorldInstance> worlds)
    {
        var zoneKey = requestZoneKey;
        if (zoneKey == 0 && zoneKeysInGroup is { Count: > 0 })
            zoneKey = zoneKeysInGroup[0];

        uint instanceId = 0;
        uint instanceIndex = 0;
        if (zoneKey != 0)
        {
            // The client sends back the instance id it was handed in the channel list, so that copy — and the
            // channel it names — wins over whichever copy of the instance happens to come first.
            WorldInstance chosen = null;
            WorldInstance first = null;
            foreach (var world in worlds)
            {
                if (world.DungeonInstance == null)
                    continue;
                if (!world.Template.ZoneKeys.Contains(zoneKey))
                    continue;

                first ??= world;
                if (catalogInstId != 0 && world.Id == catalogInstId)
                {
                    chosen = world;
                    break;
                }
            }

            var match = chosen ?? first;
            if (match != null)
            {
                instanceId = match.Id;
                instanceIndex = match.ChannelId;
            }
        }

        _ = dungeonZone;

        return new Reply(zoneKey, instanceId, instanceIndex);
    }
}
