using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

[NotInParallel]
public class DoodadRemovalResolverVerificationTests
{
    private const int SyntheticRadiusMillimeters = 1_200;
    private const int SyntheticValueTwo = 73;
    private const uint SyntheticZoneId = 4_242;
    private SingletonScope<WorldManager> _worldScope;
    private SingletonScope<FishSchoolManager> _fishScope;
    private WorldInstance _world;
    private uint _nextObjId = 1_000;
    private bool _previousZoneAuthority;
    private Action<uint> _previousRemoveRelay;
    private Action<uint, uint> _previousRemoveRelayByZone;

    [Before(Test)]
    public void Setup()
    {
        _previousZoneAuthority = WorldIntegration.ZoneAuthority;
        _previousRemoveRelay = WorldIntegration.RelayRemoveDoodadToZone;
        _previousRemoveRelayByZone = WorldIntegration.RelayRemoveDoodadToZoneId;
        WorldIntegration.ZoneAuthority = false;
        WorldIntegration.RelayRemoveDoodadToZone = null;
        WorldIntegration.RelayRemoveDoodadToZoneId = null;

        _worldScope = new SingletonScope<WorldManager>(new WorldManager(null, null, null, null, null));
        _fishScope = new SingletonScope<FishSchoolManager>(new FishSchoolManager());

        var template = new WorldTemplate
        {
            Id = 1,
            Name = "doodad-removal-verification",
            CellX = 1,
            CellY = 1,
            ZoneKeyByRegions = new uint[WorldManager.SECTORS_PER_CELL, WorldManager.SECTORS_PER_CELL]
        };
        _world = new WorldInstance(template, 0, true, 1)
        {
            Regions = new Region[WorldManager.SECTORS_PER_CELL, WorldManager.SECTORS_PER_CELL]
        };

        var worlds = (ConcurrentDictionary<uint, WorldInstance>)typeof(WorldManager)
            .GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(WorldManager.Instance)!;
        worlds[_world.Id] = _world;
    }

    [After(Test)]
    public void Teardown()
    {
        _world?.Dispose();
        WorldIntegration.ZoneAuthority = _previousZoneAuthority;
        WorldIntegration.RelayRemoveDoodadToZone = _previousRemoveRelay;
        WorldIntegration.RelayRemoveDoodadToZoneId = _previousRemoveRelayByZone;
        _fishScope.Dispose();
        _worldScope.Dispose();
    }

    [Test]
    public async Task SourceAnchor_UsesCasterAreaAndDoesNotIncludeTargetArea()
    {
        var caster = AddCharacter(1, 0f, 0f);
        var target = AddCharacter(2, 10f, 0f);
        var nearCaster = AddDoodad(DoodadOwnerType.System, 1f, 0f);
        var nearTarget = AddDoodad(DoodadOwnerType.System, 11f, 0f);
        var skill = Skill(SkillTargetSelection.Source);

        var resolved = DoodadRemovalResolver.TryGetCandidates(caster, target, skill, SyntheticRadiusMillimeters, out var candidates);

        await Assert.That(resolved).IsTrue();
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).IsEquivalentTo(new[] { nearCaster.ObjId });
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).DoesNotContain(nearTarget.ObjId);
    }

    [Test]
    public async Task TargetAnchor_IncludesDirectDoodadExactlyOnceAfterNearbyDoodads()
    {
        var caster = AddCharacter(1, 0f, 0f);
        var target = AddDoodad(DoodadOwnerType.System, 10f, 0f);
        var nearby = AddDoodad(DoodadOwnerType.System, 10.5f, 0f);
        var outside = AddDoodad(DoodadOwnerType.System, 20f, 0f);
        var skill = Skill(SkillTargetSelection.Target);

        var resolved = DoodadRemovalResolver.TryGetCandidates(caster, target, skill, SyntheticRadiusMillimeters, out var candidates);

        await Assert.That(resolved).IsTrue();
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).IsEquivalentTo(new[] { nearby.ObjId, target.ObjId });
        await Assert.That(candidates.Count(candidate => candidate.ObjId == target.ObjId)).IsEqualTo(1);
        await Assert.That(candidates[^1].ObjId).IsEqualTo(target.ObjId);
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).DoesNotContain(outside.ObjId);
    }

    [Test]
    public async Task Value1_IsConvertedFromMillimetresToMetres()
    {
        var caster = AddCharacter(1, 0f, 0f);
        var insideRadius = AddDoodad(DoodadOwnerType.System, 1.199f, 0f);
        var outsideRadius = AddDoodad(DoodadOwnerType.System, 1.201f, 0f);
        var skill = Skill(SkillTargetSelection.Source);

        var resolved = DoodadRemovalResolver.TryGetCandidates(caster, null, skill, SyntheticRadiusMillimeters, out var candidates);

        await Assert.That(resolved).IsTrue();
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).IsEquivalentTo(new[] { insideRadius.ObjId });
        await Assert.That(candidates.Select(candidate => candidate.ObjId)).DoesNotContain(outsideRadius.ObjId);
    }

    [Test]
    public async Task Execute_DeletesAndBroadcastsOnceWhileProtectingHousingAndOutOfRangeDoodads()
    {
        var session = new RecordingSession(1);
        var caster = AddCharacter(1, 0f, 0f, session);
        var barrier = AddDoodad(DoodadOwnerType.System, 1f, 0f);
        var housing = AddDoodad(DoodadOwnerType.Housing, 2f, 0f);
        var outside = AddDoodad(DoodadOwnerType.System, 10f, 0f);
        var skill = Skill(SkillTargetSelection.Source);
        session.Packets.Clear();

        Execute(caster, null, skill, SyntheticRadiusMillimeters);

        await Assert.That(_world.GetDoodad(barrier.ObjId)).IsNull();
        await Assert.That(barrier.IsVisible).IsFalse();
        await Assert.That(barrier.Region).IsNull();
        await Assert.That(_world.GetDoodad(housing.ObjId)).IsSameReferenceAs(housing);
        await Assert.That(housing.IsVisible).IsTrue();
        await Assert.That(_world.GetDoodad(outside.ObjId)).IsSameReferenceAs(outside);

        var removals = session.Packets
            .Select(SentPacket.Read)
            .Where(packet => packet.Opcode == SCOffsets.SCDoodadRemovedPacket)
            .ToArray();
        await Assert.That(removals).HasSingleItem();
        await Assert.That(new PacketStream(removals[0].Body).ReadBc()).IsEqualTo(barrier.ObjId);

        var removalCount = removals.Length;
        Execute(caster, null, skill, SyntheticRadiusMillimeters);
        var repeatedRemovals = session.Packets
            .Select(SentPacket.Read)
            .Count(packet => packet.Opcode == SCOffsets.SCDoodadRemovedPacket);
        await Assert.That(repeatedRemovals).IsEqualTo(removalCount);
    }

    [Test]
    public async Task PersistentDoodad_RelaysBeforePersistenceAndValueTwoHasNoInventedSemantics()
    {
        var zoneRemovals = new List<(uint ZoneId, uint ObjId)>();
        var unscopedRemovals = new List<uint>();
        WorldIntegration.ZoneAuthority = true;
        WorldIntegration.RelayRemoveDoodadToZoneId = (zoneId, objId) => zoneRemovals.Add((zoneId, objId));
        WorldIntegration.RelayRemoveDoodadToZone = objId => unscopedRemovals.Add(objId);

        var session = new RecordingSession(1);
        var caster = AddCharacter(1, 0f, 0f, session);
        var zeroValue = AddPersistentDoodad(1f, 0f);
        var nonzeroValue = AddPersistentDoodad(10f, 0f);
        var skill = Skill(SkillTargetSelection.Source);
        session.Packets.Clear();

        Execute(caster, null, skill, SyntheticRadiusMillimeters, 0);
        caster.SetPosition(10f, 0f, 0f, 0f, 0f, 0f);
        Execute(caster, null, skill, SyntheticRadiusMillimeters, SyntheticValueTwo);

        await Assert.That(zoneRemovals).HasCount().EqualTo(2);
        await Assert.That(zoneRemovals[0]).IsEqualTo((SyntheticZoneId, zeroValue.ObjId));
        await Assert.That(zoneRemovals[1]).IsEqualTo((SyntheticZoneId, nonzeroValue.ObjId));
        await Assert.That(unscopedRemovals).IsEmpty();

        await AssertPersistentDeletion(zeroValue);
        await AssertPersistentDeletion(nonzeroValue);
    }

    private async Task AssertPersistentDeletion(RecordingPersistentDoodad doodad)
    {
        await Assert.That(doodad.DeletedDbIds).IsEquivalentTo(new[] { doodad.DbId });
        await Assert.That(doodad.SpawnManagerUnlinkCalls).IsEqualTo(1);
        await Assert.That(doodad.IsPersistent).IsFalse();
        await Assert.That(doodad.IsVisible).IsFalse();
        await Assert.That(_world.GetDoodad(doodad.ObjId)).IsNull();
    }

    private Character AddCharacter(uint id, float x, float y, RecordingSession session = null)
    {
        var character = new Character(new UnitCustomModelParams())
        {
            Id = id,
            ObjId = id,
            Name = $"Verifier{id}"
        };
        if (session != null)
            character.Connection = new GameConnection(session) { ActiveChar = character };

        Place(character, x, y);
        return character;
    }

    private Doodad AddDoodad(DoodadOwnerType ownerType, float x, float y)
    {
        var objId = _nextObjId++;
        var templateId = objId + 10_000;
        var doodad = new Doodad
        {
            ObjId = objId,
            TemplateId = templateId,
            Template = new DoodadTemplate { Id = templateId },
            OwnerType = ownerType,
            IsPersistent = false
        };
        Place(doodad, x, y);
        return doodad;
    }

    private RecordingPersistentDoodad AddPersistentDoodad(float x, float y)
    {
        var objId = _nextObjId++;
        var templateId = objId + 10_000;
        var doodad = new RecordingPersistentDoodad
        {
            ObjId = objId,
            DbId = objId + 20_000,
            TemplateId = templateId,
            Template = new DoodadTemplate { Id = templateId },
            OwnerType = DoodadOwnerType.System,
            IsPersistent = true
        };
        doodad.Transform.GetType()
            .GetField("_zoneId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(doodad.Transform, SyntheticZoneId);
        Place(doodad, x, y);
        return doodad;
    }

    private void Place(GameObject gameObject, float x, float y)
    {
        gameObject.ParentWorld = _world;
        _world.AddObject(gameObject);
        gameObject.IsVisible = true;
        gameObject.SetPosition(x, y, 0f, 0f, 0f, 0f);
    }

    private static Skill Skill(SkillTargetSelection targetSelection) =>
        new() { Template = new SkillTemplate { TargetSelection = targetSelection } };

    private static void Execute(BaseUnit caster, BaseUnit target, Skill skill, int radiusMillimeters, int value2 = 0) =>
        new RemoveAllDoodad().Execute(caster, null!, target, null!, null!, skill, null!, DateTime.UtcNow,
            radiusMillimeters, value2, 0, 0);

    private sealed class RecordingPersistentDoodad : Doodad
    {
        public List<uint> DeletedDbIds { get; } = [];
        public int SpawnManagerUnlinkCalls { get; private set; }

        protected override void DeletePersistentRow() => DeletedDbIds.Add(DbId);

        protected override void RemoveFromSpawnManager() => SpawnManagerUnlinkCalls++;
    }

    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => sessionId;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }
}
