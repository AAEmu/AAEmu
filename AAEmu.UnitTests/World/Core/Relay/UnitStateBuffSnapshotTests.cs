using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C.UnitState;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class UnitStateBuffSnapshotTests
{
    private static List<Buff> Effects(Character owner) => (List<Buff>)typeof(Buffs)
        .GetField("_effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner.Buffs)!;

    private static Buff Add(Character owner, uint index, BuffKind kind, bool passive = false)
    {
        var buff = new Buff(owner, owner, new SkillCasterUnit(owner.ObjId),
            new BuffTemplate { Id = index + 2400, Kind = kind, StackRule = BuffStackRule.Multiple },
            null, DateTime.UtcNow)
        {
            Index = index, Stack = 3, InUse = true, State = EffectState.Acting, Passive = passive
        };
        Effects(owner).Add(buff);
        return buff;
    }

    [Test]
    public async Task SnapshotTracksExactlyTheWireEntriesWithLimitsAndPassiveFiltering()
    {
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 1698, Level = 40 };
        uint next = 1;
        foreach (var kind in new[] { BuffKind.Good, BuffKind.Bad, BuffKind.Hidden })
            for (var i = 0; i < 35; i++)
                Add(owner, next++, kind);
        var passive = Add(owner, next, BuffKind.Good, passive: true);
        var written = new List<UnitStateBuffSerializer.SnapshotEntry>();
        var stream = new PacketStream();
        UnitStateBuffSerializer.Write(stream, owner, written);
        var wire = new PacketStream(stream.GetBytes());
        var decoded = new List<(uint Index, uint Stack)>();
        foreach (var expectedCount in new byte[] { 32, 20, 28 })
        {
            var count = wire.ReadByte();
            await Assert.That(count).IsEqualTo(expectedCount);
            for (var i = 0; i < count; i++)
            {
                var index = wire.ReadUInt32();
                await Assert.That(wire.ReadByte()).IsEqualTo((byte)SkillCasterType.Unit);
                wire.ReadBc();
                wire.ReadUInt64();
                wire.ReadByte();
                wire.ReadUInt16();
                wire.ReadPisc(4);
                var identity = wire.ReadPisc(4);
                decoded.Add((index, identity[1]));
            }
        }
        await Assert.That(decoded).IsEquivalentTo(written.Select(e => (e.Index, e.Stack)).ToList());
        await Assert.That(written.Any(e => e.Buff == passive)).IsFalse();
        await Assert.That(written.All(e => !e.Buff.RelayedToZone)).IsTrue();
    }

    [Test]
    public async Task LoginSnapshotKeepsRemovalAndUpdateEligibleAfterRegistryReset()
    {
        const uint zone = 310142;
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 1698, Level = 40 };
        var protection = Add(owner, 23, BuffKind.Good); // template 2423, login protection
        var stream = new PacketStream();
        var written = new List<UnitStateBuffSerializer.SnapshotEntry>();
        UnitStateBuffSerializer.Write(stream, owner, written);

        ZoneBuffRegistry.MarkCreated(zone, 0, owner.ObjId, 999);
        ZoneBuffRegistry.ClearUnit(zone, 0, owner.ObjId);
        ZoneBuffRegistry.MarkSnapshot(zone, 0, owner.ObjId, written);
        try
        {
            await Assert.That(BuffCreatedWire.ShouldRelayRemoved(protection, out _)).IsTrue();
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, owner.ObjId, protection.Index)).IsTrue();
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, owner.ObjId, 999)).IsFalse();
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 1, owner.ObjId, protection.Index)).IsFalse();
            ZoneBuffRegistry.TryGetRecordedStack(zone, 0, owner.ObjId, protection.Index, out var stack);
            await Assert.That(stack).IsEqualTo(3u);
            ZoneBuffRegistry.Clear(zone, 0, owner.ObjId, protection.Index);
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, owner.ObjId, protection.Index)).IsFalse();
        }
        finally { ZoneBuffRegistry.ResetZone(zone, 0); }
    }
}
