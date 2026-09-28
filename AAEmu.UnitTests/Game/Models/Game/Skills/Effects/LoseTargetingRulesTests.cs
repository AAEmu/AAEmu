using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// lose_targeting_the_target (type 146) drops the affected unit's own target and — for the units that
/// currently hold <em>it</em> as their target — theirs, but only the ones an active buff on the affected
/// unit actually bars. A unit nobody is barred from keeps every holder's target, and that is what
/// these tests pin: every fixture carries units that MUST come out untouched, so a filter that filters
/// nothing fails here rather than passing.
/// </summary>
/// <remarks>
/// The relations used below are the ones the targeting helper answers from the units themselves, so
/// the fixture needs no faction, zone or team content. <c>expedition_member</c> is the narrow one: the
/// barred class is "same expedition", so a holder in a different expedition and a holder with no
/// expedition at all both have to survive the cast.
/// </remarks>
[NotInParallel]
public class LoseTargetingRulesTests
{
    private const uint RestrictionBuffId = 900001;
    private const uint SecondRestrictionBuffId = 900002;
    private const uint OwnerObjId = 1;
    private const uint BarredHolderObjId = 2;
    private const uint KeptHolderObjId = 3;
    private const uint ExpeditionlessHolderObjId = 4;
    private const uint BystanderObjId = 5;

    private const FactionsEnum NuiaExpedition = FactionsEnum.NuiaAlliance;
    private const FactionsEnum HaranyaExpedition = FactionsEnum.HaranyaAlliance;

    private SingletonScope<BuffGameData> _buffGameData;
    private SingletonScope<SkillManager> _skills;
    private SingletonScope<WorldManager> _worlds;
    private Dictionary<uint, BuffTemplate> _templates;
    private bool _previousZoneAuthority;
    private Action<uint, uint, bool> _previousRelay;

    [Before(Test)]
    public void InstallContentLookups()
    {
        // Adding a buff reads buff_modifiers (BuffGameData) and the buff's own template plus its tags
        // (SkillManager); with no content loaded both have to answer rather than be null.
        var gameData = new BuffGameData();
        SetField(gameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(gameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(gameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        _buffGameData = new SingletonScope<BuffGameData>(gameData);

        // Buffs.AddBuff reads the template back by its own id to decide whether the arrival interrupts
        // the owner, so the templates the fixture applies have to be in the table.
        _templates = [];
        var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_buffs", _templates);
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(skillManager, "_taggedBuffs", new Dictionary<uint, List<uint>>());
        _skills = new SingletonScope<SkillManager>(skillManager);

        // ParentWorld resolves the world instance through WorldManager, so one has to answer before any
        // unit is placed in an instance.
        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance>());
        _worlds = new SingletonScope<WorldManager>(worlds);

        _previousZoneAuthority = WorldIntegration.ZoneAuthority;
        _previousRelay = WorldIntegration.RelayTargetChangedToZone;
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        WorldIntegration.ZoneAuthority = _previousZoneAuthority;
        WorldIntegration.RelayTargetChangedToZone = _previousRelay;
        _worlds.Dispose();
        _skills.Dispose();
        _buffGameData.Dispose();
    }

    #region fixtures

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static Unit InExpedition(uint objId, FactionsEnum? expedition) => new()
    {
        ObjId = objId,
        Expedition = expedition.HasValue ? new Expedition { Id = expedition.Value } : null
    };

    /// <summary>
    /// The affected unit and four candidates: one the restriction bars, two it must not, and one that
    /// is not holding the affected unit at all.
    /// </summary>
    private static (Unit Affected, Unit Barred, Unit Kept, Unit Expeditionless, Unit Bystander) TargetSet()
    {
        var affected = InExpedition(OwnerObjId, NuiaExpedition);
        var barred = InExpedition(BarredHolderObjId, NuiaExpedition);
        var kept = InExpedition(KeptHolderObjId, HaranyaExpedition);
        var expeditionless = InExpedition(ExpeditionlessHolderObjId, null);
        var bystander = InExpedition(BystanderObjId, NuiaExpedition);

        barred.CurrentTarget = affected;
        kept.CurrentTarget = affected;
        expeditionless.CurrentTarget = affected;
        bystander.CurrentTarget = InExpedition(90, NuiaExpedition); // somebody else entirely

        return (affected, barred, kept, expeditionless, bystander);
    }

    private void ApplyBuff(BaseUnit owner, BuffTemplate template, Unit caster)
    {
        _templates[template.Id] = template;
        owner.Buffs.AddBuff(new Buff(owner, caster, new SkillCasterUnit(caster?.ObjId ?? 0), template, null,
            DateTime.UtcNow)
        {
            Passive = true, // keeps SCBuffCreated/SCBuffRemoved and the zone relay out of a test
            AbLevel = 1
        });
    }

    private static BuffTemplate Restriction(
        uint relationId,
        bool impossibleTargeting = true,
        bool impossibleChangeTargeting = false,
        bool useOriginSource = false,
        uint buffId = RestrictionBuffId) => new()
        {
            Id = buffId,
            Duration = 0,
            ImpossibleTargeting = impossibleTargeting,
            ImpossibleChangeTargeting = impossibleChangeTargeting,
            TargetingRelationId = relationId,
            TargetingUseOriginSource = useOriginSource
        };

    private static void RunEffect(BaseUnit target) =>
        new LoseTargetingTheTarget().Execute(
            caster: null, casterObj: null, target, targetObj: null, castObj: null,
            skill: null, skillObject: null, time: DateTime.UtcNow,
            value1: 4, value2: 0, value3: 0, value4: 0);

    /// <summary>
    /// The object ids a selection is made of. A unit's own properties reach the model catalog, so a
    /// selection is compared by id rather than by inspecting the units.
    /// </summary>
    private static uint[] IdsOf(IEnumerable<Unit> units) => [.. units.Select(unit => unit.ObjId)];

    /// <summary>The object id a unit currently holds as its target; 0 when it holds none.</summary>
    private static uint TargetOf(Unit unit) => unit.CurrentTarget?.ObjId ?? 0;

    private WorldInstance NewWorld(string name)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = name }, 0, true, 1);
        Worlds().TryAdd(world.Id, world);
        return world;
    }

    private ConcurrentDictionary<uint, WorldInstance> Worlds()
        => (ConcurrentDictionary<uint, WorldInstance>)typeof(WorldManager)
            .GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(WorldManager.Instance)!;

    /// <summary>Registers the units with the instance the effect enumerates its holders from.</summary>
    private static void Place(WorldInstance world, params Unit[] units)
    {
        foreach (var unit in units)
        {
            unit.ParentWorld = world;
            world.AddObject(unit);
        }
    }

    #endregion

    #region catalog resolution

    [Test]
    [Arguments(0u, SkillTargetRelation.Any)]
    [Arguments(1u, SkillTargetRelation.Friendly)]
    [Arguments(4u, SkillTargetRelation.Hostile)]
    [Arguments(5u, SkillTargetRelation.Others)]
    [Arguments(8u, SkillTargetRelation.Family)]
    [Arguments(10u, SkillTargetRelation.ExpeditionMember)]
    public async Task ACatalogRelationIdResolves(uint relationId, SkillTargetRelation expected)
    {
        await Assert.That(LoseTargetingRules.TryResolveRelation(relationId, out var relation)).IsTrue();
        await Assert.That(relation).IsEqualTo(expected);
    }

    [Test]
    [Arguments(11u)]
    [Arguments(200u)]
    [Arguments(255u)]
    [Arguments(256u)]
    [Arguments(uint.MaxValue)]
    public async Task AnIdOutsideTheCatalogDoesNotResolve(uint relationId)
    {
        // Narrowing to a byte first is what keeps 255 apart from 256: wrapping the width would have
        // turned 256 into 0, which is Any — the widest bar there is.
        await Assert.That(LoseTargetingRules.TryResolveRelation(relationId, out _)).IsFalse();
    }

    #endregion

    #region the filter has to be narrow

    [Test]
    public async Task ARestrictionSelectsOnlyTheHoldersItBars()
    {
        var (affected, barred, kept, expeditionless, bystander) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(
            affected, [affected, barred, kept, expeditionless, bystander]);

        await Assert.That(IdsOf(selected)).IsEquivalentTo(new[] { barred.ObjId });
    }

    [Test]
    public async Task AHolderTheRestrictionDoesNotBarKeepsItsTarget()
    {
        // The whole point of the reversed half: the same cast must not take the target away from a
        // holder the buff does not name. A filter that selected every holder would zero these.
        WorldIntegration.ZoneAuthority = false;
        using var world = NewWorld("lose-targeting-narrow-test");
        var (affected, barred, kept, expeditionless, _) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), InExpedition(99, NuiaExpedition));
        Place(world, affected, barred, kept, expeditionless);

        RunEffect(affected);

        await Assert.That(TargetOf(affected)).IsEqualTo(0u);      // the direct half still runs
        await Assert.That(TargetOf(barred)).IsEqualTo(0u);       // the barred holder loses its target
        await Assert.That(TargetOf(kept)).IsEqualTo(affected.ObjId);
        await Assert.That(TargetOf(expeditionless)).IsEqualTo(affected.ObjId);
    }

    [Test]
    public async Task AUnitWithNoTargetingRestrictionBarsNobody()
    {
        var (affected, _, kept, _, bystander) = TargetSet();

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, kept, bystander]);

        await Assert.That(selected).IsEmpty();
    }

    [Test]
    public async Task ABuffThatOnlyForbidsChangingTargetingBarsNobody()
    {
        // impossible_change_targeting is about the affected unit's own target, not about who else may
        // hold it, so it must not widen the reversed selection to every holder.
        var (affected, barred, kept, _, bystander) = TargetSet();
        ApplyBuff(affected, Restriction(
            (uint)SkillTargetRelation.ExpeditionMember,
            impossibleTargeting: false,
            impossibleChangeTargeting: true), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, barred, kept, bystander]);

        await Assert.That(selected).IsEmpty();
    }

    [Test]
    public async Task AnUnreadableRelationIdBarsNobodyRatherThanEverybody()
    {
        // Skipping an id this build cannot read is the narrow outcome. Reading it as Any would empty
        // the target of every player who happened to have this unit selected.
        var (affected, barred, kept, _, bystander) = TargetSet();
        ApplyBuff(affected, Restriction(9999), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, barred, kept, bystander]);

        await Assert.That(selected).IsEmpty();
    }

    [Test]
    public async Task AHolderThatIsNotTargetingTheAffectedUnitIsNeverSelected()
    {
        var (affected, _, _, _, bystander) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, bystander]);

        await Assert.That(selected).IsEmpty();
    }

    [Test]
    public async Task TheAffectedUnitIsNeverOneOfItsOwnHolders()
    {
        var affected = InExpedition(OwnerObjId, NuiaExpedition);
        affected.CurrentTarget = affected;
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(affected, [affected]);

        await Assert.That(selected).IsEmpty();
    }

    [Test]
    public async Task AnAffectedUnitInNoKnownWorldSelectsNobody()
    {
        var (affected, barred, _, _, _) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));

        await Assert.That(LoseTargetingRules.SelectHolders(affected, null)).IsEmpty();
        await Assert.That(LoseTargetingRules.SelectHolders(affected, [])).IsEmpty();
    }

    #endregion

    #region the widest bars, and the relation two shipped rows name

    [Test]
    public async Task AnAnyRelationBarsEveryHolderAndOnlyHolders()
    {
        var (affected, barred, kept, expeditionless, bystander) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(
            affected, [affected, barred, kept, expeditionless, bystander]);

        await Assert.That(IdsOf(selected))
            .IsEquivalentTo(new[] { barred.ObjId, kept.ObjId, expeditionless.ObjId });
    }

    [Test]
    public async Task AnOthersRelationBarsEveryHolderAndOnlyHolders()
    {
        // The helper answers "somebody other than me", so the owner itself is the only survivor of the
        // relation — and it is never one of its own holders.
        var (affected, barred, kept, expeditionless, bystander) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Others), InExpedition(99, NuiaExpedition));

        var selected = LoseTargetingRules.SelectHolders(
            affected, [affected, barred, kept, expeditionless, bystander]);

        await Assert.That(IdsOf(selected))
            .IsEquivalentTo(new[] { barred.ObjId, kept.ObjId, expeditionless.ObjId });
    }

    #endregion

    #region the scope axis: whose relation is measured

    [Test]
    public async Task TheOriginSourceScopeMeasuresTheRelationFromTheBuffCaster()
    {
        // The owner is moved into the OTHER expedition first, so "same as the owner" and "same as the
        // caster" cannot both hold for the same holder and the two scopes cannot be confused.
        var (affected, barred, kept, _, bystander) = TargetSet();
        affected.Expedition = new Expedition { Id = HaranyaExpedition };
        var caster = InExpedition(99, NuiaExpedition);
        ApplyBuff(affected,
            Restriction((uint)SkillTargetRelation.ExpeditionMember, useOriginSource: true), caster);

        var restrictions = LoseTargetingRules.CollectRestrictions(affected);

        await Assert.That(restrictions.Count).IsEqualTo(1);
        await Assert.That(restrictions[0].Subject).IsEqualTo(TargetingRestrictionSubject.OriginSource);
        await Assert.That(restrictions[0].RelationSubject.ObjId).IsEqualTo(caster.ObjId);

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, barred, kept, bystander]);

        await Assert.That(IdsOf(selected)).IsEquivalentTo(new[] { barred.ObjId });
    }

    [Test]
    public async Task WithoutTheOriginSourceFlagTheRelationIsMeasuredFromTheOwner()
    {
        var (affected, barred, kept, _, bystander) = TargetSet();
        affected.Expedition = new Expedition { Id = HaranyaExpedition };
        var caster = InExpedition(99, NuiaExpedition);
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), caster);

        var restrictions = LoseTargetingRules.CollectRestrictions(affected);

        await Assert.That(restrictions[0].Subject).IsEqualTo(TargetingRestrictionSubject.Owner);
        await Assert.That(restrictions[0].RelationSubject.ObjId).IsEqualTo(affected.ObjId);

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, barred, kept, bystander]);

        await Assert.That(IdsOf(selected)).IsEquivalentTo(new[] { kept.ObjId });
    }

    [Test]
    public async Task AnOriginSourceScopeWithoutACasterFallsBackToTheOwner()
    {
        // The flag names a subject that is not there. Measuring from the owner keeps the restriction
        // measurable, instead of leaving a rule that can neither bar nor be resolved.
        var (affected, barred, _, _, bystander) = TargetSet();
        ApplyBuff(affected,
            Restriction((uint)SkillTargetRelation.ExpeditionMember, useOriginSource: true), null);

        var restrictions = LoseTargetingRules.CollectRestrictions(affected);

        await Assert.That(restrictions.Count).IsEqualTo(1);
        await Assert.That(restrictions[0].Subject).IsEqualTo(TargetingRestrictionSubject.Owner);
        await Assert.That(restrictions[0].RelationSubject.ObjId).IsEqualTo(affected.ObjId);

        var selected = LoseTargetingRules.SelectHolders(affected, [affected, barred, bystander]);

        await Assert.That(IdsOf(selected)).IsEquivalentTo(new[] { barred.ObjId });
    }

    [Test]
    public async Task EveryRestrictionIsReportedSeparately()
    {
        var (affected, _, _, _, _) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), InExpedition(99, NuiaExpedition));
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Others, buffId: SecondRestrictionBuffId),
            InExpedition(99, NuiaExpedition));

        var restrictions = LoseTargetingRules.CollectRestrictions(affected);

        await Assert.That(restrictions.Count).IsEqualTo(2);
        await Assert.That(restrictions[0].BarredRelation).IsEqualTo(SkillTargetRelation.ExpeditionMember);
        await Assert.That(restrictions[1].BarredRelation).IsEqualTo(SkillTargetRelation.Others);
    }

    [Test]
    public async Task ARuleWithNoSubjectBarsNobody()
    {
        var holder = InExpedition(BarredHolderObjId, NuiaExpedition);
        var rule = new TargetingRestriction(
            SkillTargetRelation.ExpeditionMember, null, TargetingRestrictionSubject.OriginSource);

        await Assert.That(LoseTargetingRules.Bars(rule, holder)).IsFalse();
        await Assert.That(LoseTargetingRules.Bars(rule, null)).IsFalse();
    }

    #endregion

    #region the effect over a live world instance

    [Test]
    public async Task TheEffectEnumeratesTheHoldersOutOfTheWorldInstance()
    {
        WorldIntegration.ZoneAuthority = false;
        using var world = NewWorld("lose-targeting-test");
        var (affected, barred, kept, _, bystander) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), InExpedition(99, NuiaExpedition));
        Place(world, affected, barred, kept, bystander);

        RunEffect(affected);

        await Assert.That(TargetOf(affected)).IsEqualTo(0u);
        await Assert.That(TargetOf(barred)).IsEqualTo(0u);
        await Assert.That(TargetOf(kept)).IsEqualTo(affected.ObjId);
        await Assert.That(TargetOf(bystander)).IsNotEqualTo(0u);
    }

    [Test]
    public async Task UnderZoneAuthorityOnlyTheBarredHoldersAreRelayed()
    {
        WorldIntegration.ZoneAuthority = true;
        var relayed = new List<(uint ObjId, uint Target, bool ForceByWorld)>();
        WorldIntegration.RelayTargetChangedToZone = (objId, target, forceByWorld) =>
            relayed.Add((objId, target, forceByWorld));

        using var world = NewWorld("lose-targeting-relay-test");
        var (affected, barred, kept, _, _) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.ExpeditionMember), InExpedition(99, NuiaExpedition));
        Place(world, affected, barred, kept);

        RunEffect(affected);

        await Assert.That(relayed.Select(entry => entry.ObjId))
            .IsEquivalentTo(new[] { affected.ObjId, barred.ObjId });
        await Assert.That(relayed.TrueForAll(entry => entry.Target == 0 && entry.ForceByWorld)).IsTrue();
        // The World mirror is left to the Zone's response, so the holder that was not barred is still
        // holding its target here as well.
        await Assert.That(TargetOf(kept)).IsEqualTo(affected.ObjId);
    }

    [Test]
    public async Task AnAffectedUnitNobodyIsBarredFromStillLosesItsOwnTarget()
    {
        // The direct half does not wait on the reversed half: a self-cast that lands on a unit with no
        // targeting restriction still clears that unit's own target, and the holders in the same world
        // keep theirs.
        WorldIntegration.ZoneAuthority = false;
        using var world = NewWorld("lose-targeting-unbarred-test");
        var (affected, barred, kept, expeditionless, _) = TargetSet();
        Place(world, affected, barred, kept, expeditionless);

        RunEffect(affected);

        await Assert.That(TargetOf(affected)).IsEqualTo(0u);
        await Assert.That(TargetOf(barred)).IsEqualTo(affected.ObjId);
        await Assert.That(TargetOf(kept)).IsEqualTo(affected.ObjId);
        await Assert.That(TargetOf(expeditionless)).IsEqualTo(affected.ObjId);
    }

    [Test]
    public async Task BothDirectionsOfAMutualSelectionAreCleared()
    {
        // Two units holding each other: the enumeration happens before anything is cleared, so the
        // holder is still selected after the affected unit's own target has already gone.
        WorldIntegration.ZoneAuthority = false;
        using var world = NewWorld("lose-targeting-mutual-test");
        var (affected, barred, _, _, _) = TargetSet();
        affected.CurrentTarget = barred;
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));
        Place(world, affected, barred);

        RunEffect(affected);

        await Assert.That(TargetOf(affected)).IsEqualTo(0u);
        await Assert.That(TargetOf(barred)).IsEqualTo(0u);
    }

    [Test]
    public async Task AnAffectedUnitOutsideAnyWorldInstanceStillLosesItsOwnTarget()
    {
        WorldIntegration.ZoneAuthority = false;
        var (affected, barred, _, _, _) = TargetSet();
        ApplyBuff(affected, Restriction((uint)SkillTargetRelation.Any), InExpedition(99, NuiaExpedition));

        RunEffect(affected);

        // Nothing can be enumerated, so the holder that this instance does not hold is left alone rather
        // than guessed at.
        await Assert.That(TargetOf(affected)).IsEqualTo(0u);
        await Assert.That(TargetOf(barred)).IsEqualTo(affected.ObjId);
    }

    #endregion
}
