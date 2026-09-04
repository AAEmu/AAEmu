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
            foreach (var world in worlds)
            {
                if (world.DungeonInstance == null)
                    continue;
                if (!world.Template.ZoneKeys.Contains(zoneKey))
                    continue;

                instanceId = world.Id;
                instanceIndex = world.ChannelId;
                break;
            }
        }

        _ = catalogInstId;
        _ = dungeonZone;

        return new Reply(zoneKey, instanceId, instanceIndex);
    }
}
