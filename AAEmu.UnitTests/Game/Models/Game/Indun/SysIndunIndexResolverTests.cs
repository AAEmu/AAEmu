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

    [Test]
    public async Task Resolve_PrefersTheCopyTheClientPicked()
    {
        // The channel list hands out copy ids; the picker sends one back, and that copy's channel is the answer.
        var worlds = new[]
        {
            MakeDungeonWorld(worldId: 42, channelId: 3, zoneKey: 280),
            MakeDungeonWorld(worldId: 99, channelId: 7, zoneKey: 280)
        };

        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 42,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds);

        await Assert.That(reply.InstanceId).IsEqualTo(42u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(3u);
    }

    [Test]
    public async Task Resolve_FallsBackToTheFirstCopy_WhenThePickIsUnknown()
    {
        // A free-channel row carries no copy, so nothing matches and the first copy is the best answer there is.
        var worlds = new[]
        {
            MakeDungeonWorld(worldId: 42, channelId: 3, zoneKey: 280),
            MakeDungeonWorld(worldId: 99, channelId: 7, zoneKey: 280)
        };

        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 0,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds);

        await Assert.That(reply.InstanceId).IsEqualTo(42u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(3u);
    }

    [Test]
    public async Task Resolve_IgnoresCopiesNoHostIsServing()
    {
        // A copy nothing is hosting cannot be entered, so a row that named one resolves to no copy at all
        // rather than to that copy — remembering it would send the entry after this one nowhere.
        var worlds = new[] { MakeDungeonWorld(worldId: 99, channelId: 7, zoneKey: 280) };

        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 99,
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds,
            isHosted: (_, worldId) => worldId != 99);

        await Assert.That(reply.InstanceId).IsEqualTo(0u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(0u);
    }

    [Test]
    public async Task Resolve_FallsBackOnlyToACopyAHostIsServing()
    {
        var worlds = new[]
        {
            MakeDungeonWorld(worldId: 42, channelId: 3, zoneKey: 280),
            MakeDungeonWorld(worldId: 99, channelId: 7, zoneKey: 280)
        };

        var reply = SysIndunIndexResolver.Resolve(
            requestZoneKey: 280,
            catalogInstId: 999, // gone
            dungeonZone: new IndunZone { ZoneGroupId = 58 },
            zoneKeysInGroup: [280u],
            worlds,
            isHosted: (_, worldId) => worldId == 99);

        await Assert.That(reply.InstanceId).IsEqualTo(99u);
        await Assert.That(reply.InstanceIndex).IsEqualTo(7u);
    }
}
