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

[NotInParallel]
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
    public async Task MixedSnapshotPreservesZoneBuffsOnWireWithoutRegisteringThemForWorldRelay()
    {
        const uint zone = 310143;
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 1698, Level = 40 };
        var worldBuff = Add(owner, 23, BuffKind.Good);
        var zoneBuff = Add(owner, 24, BuffKind.Bad);
        zoneBuff.ZoneAuthored = true;
        var written = new List<UnitStateBuffSerializer.SnapshotEntry>();
        UnitStateBuffSerializer.Write(new PacketStream(), owner, written);
        try
        {
            ZoneBuffRegistry.MarkSnapshot(zone, 0, owner.ObjId, written);
            await Assert.That(written.Select(e => e.Index)).IsEquivalentTo(new uint[] { 23, 24 });
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, owner.ObjId, worldBuff.Index)).IsTrue();
            await Assert.That(worldBuff.RelayedToZone).IsTrue();
            await Assert.That(ZoneBuffRegistry.WasCreated(zone, 0, owner.ObjId, zoneBuff.Index)).IsFalse();
            await Assert.That(zoneBuff.RelayedToZone).IsFalse();
        }
        finally { ZoneBuffRegistry.ResetZone(zone, 0); }
    }

    [Test]
    public async Task SnapshotRetirementOnlyRelaysEndedEligibleWorldBuffs()
    {
        const uint zone = 310144;
        var owner = new Character(new UnitCustomModelParams()) { ObjId = 1698, Level = 40 };
        var worldBuff = Add(owner, 23, BuffKind.Good);
        var zoneBuff = Add(owner, 24, BuffKind.Bad);
        zoneBuff.ZoneAuthored = true;
        var activeBuff = Add(owner, 25, BuffKind.Good);
        var written = new List<UnitStateBuffSerializer.SnapshotEntry>();
        UnitStateBuffSerializer.Write(new PacketStream(), owner, written);
        var originalRelay = AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone;
        var removals = new List<(uint Owner, uint Index)>();
        try
        {
            ZoneBuffRegistry.MarkSnapshot(zone, 0, owner.ObjId, written);
            // Simulate expiration after serialization; even a previously relayed Zone buff
            // must not be echoed back to its authority by the race cleanup.
            worldBuff.State = EffectState.Finished;
            zoneBuff.State = EffectState.Finished;
            zoneBuff.RelayedToZone = true;
            AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone = (id, index) => removals.Add((id, index));
            PlayerEnterService.RetireEndedSnapshotBuffs(owner.ObjId, written);
            await Assert.That(removals).IsEquivalentTo(new[] { (owner.ObjId, worldBuff.Index) });
            await Assert.That(activeBuff.IsEnded()).IsFalse();
        }
        finally
        {
            AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone = originalRelay;
            ZoneBuffRegistry.ResetZone(zone, 0);
        }
    }

    [Test]
    [Arguments(1698u, false)]
    [Arguments(0u, true)]
    public async Task SnapshotRetirementRejectsUnrelayedOrInvalidOwner(uint ownerId, bool relayed)
    {
        var owner = new Character(new UnitCustomModelParams()) { ObjId = ownerId, Level = 40 };
        var buff = Add(owner, 23, BuffKind.Good);
        buff.State = EffectState.Finished;
        buff.RelayedToZone = relayed;
        var originalRelay = AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone;
        var calls = 0;
        try
        {
            AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone = (_, _) => calls++;
            PlayerEnterService.RetireEndedSnapshotBuffs(ownerId,
                [new UnitStateBuffSerializer.SnapshotEntry(buff, buff.Index, 3)]);
            await Assert.That(calls).IsEqualTo(0);
        }
        finally { AAEmu.Game.WorldIntegration.RelayBuffRemovedToZone = originalRelay; }
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
