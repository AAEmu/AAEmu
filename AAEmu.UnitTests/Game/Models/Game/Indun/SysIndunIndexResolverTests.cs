using System.Runtime.CompilerServices;

using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class SysIndunIndexResolverTests
{
    private static WorldInstance MakeDungeonWorld(uint worldId, uint channelId, uint zoneKey)
    {
        var template = new WorldTemplate
        {
            Name = "test_instance",
            ZoneKeys = [zoneKey]
        };
        var world = new WorldInstance(template, channelId, dontFreeInstanceId: true, instanceId: worldId);
        // Resolver only checks non-null; Dungeon has no parameterless ctor.
        world.DungeonInstance = (Dungeon)RuntimeHelpers.GetUninitializedObject(typeof(Dungeon));
        return world;
    }

    [Test]
    public async Task Resolve_UsesRequestZoneKey_WhenProvided()
    {
        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 23,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds: []);

        await Assert.That(reply.ZoneKey).IsEqualTo(280u);
        await Assert.That(reply.InstanceId).IsEqualTo(0u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(0u);
    }

    [Test]
    public async Task Resolve_FallsBackToFirstGroupZoneKey_WhenRequestIsZero()
    {
        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 0,
            catalogInstId: 23,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u, 281u],
            worlds: []);

        await Assert.That(reply.ZoneKey).IsEqualTo(280u);
    }

    [Test]
    public async Task Resolve_FindsExistingCopy_ByZoneKey()
    {
        var worlds = new[]
        {
            MakeDungeonWorld(worldId: 42, channelId: 3, zoneKey: 265),
            MakeDungeonWorld(worldId: 99, channelId: 7, zoneKey: 280)
        };

        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 23,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds);

        await Assert.That(reply.InstanceId).IsEqualTo(99u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(7u);
    }
}
