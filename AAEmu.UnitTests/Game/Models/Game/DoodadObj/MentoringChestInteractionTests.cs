using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

/// <summary>
/// Akmit's Secret Chest (doodad 7660) is representative of the four authored mentoring chests.
/// Its content graph requires mentor skill 24668, then mentee skill 24667, then mentor skill 24668.
/// </summary>
[NotInParallel]
public sealed class MentoringChestInteractionTests
{
    private const uint MentorSkill = 24668;
    private const uint MenteeSkill = 24667;
    private const uint MentorActivationPhase = 21050;
    private const uint MenteeGatePhase = 20859;
    private const uint MenteeLootPhase = 20860;
    private const uint MentorGatePhase = 20861;
    private const uint MentorLootPhase = 20862;
    private const uint FinalPhase = 20863;

    private FieldInfo _singletonField;
    private object _previousManager;
    private SingletonScope<SkillManager> _skillManagerScope;
    private DoodadManager _manager;
    private TrackingLootItem _menteeLoot;
    private TrackingLootItem _mentorLoot;

    [Before(Test)]
    public void Setup()
    {
        _singletonField = typeof(Singleton<DoodadManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousManager = _singletonField.GetValue(null);
        _skillManagerScope = new SingletonScope<SkillManager>(new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object));

        _manager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);

        _menteeLoot = new TrackingLootItem { Id = 3067, ItemId = 31592 };
        _mentorLoot = new TrackingLootItem { Id = 3068, ItemId = 31591 };
        SetField(_manager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [MentorActivationPhase] = [Func(1, 2749, nameof(DoodadFuncFakeUse), MenteeGatePhase)],
            [MenteeGatePhase] = [Func(2, 2725, nameof(DoodadFuncFakeUse), MenteeLootPhase)],
            [MenteeLootPhase] = [Func(3, 3067, nameof(DoodadFuncLootItem), MentorGatePhase)],
            [MentorGatePhase] = [Func(4, 2726, nameof(DoodadFuncFakeUse), MentorLootPhase)],
            [MentorLootPhase] = [Func(5, 3068, nameof(DoodadFuncLootItem), FinalPhase)]
        });
        SetField(_manager, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        SetField(_manager, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncFakeUse)] = new()
            {
                [2749] = new DoodadFuncFakeUse { Id = 2749, FakeSkillId = MentorSkill },
                [2725] = new DoodadFuncFakeUse { Id = 2725, FakeSkillId = MenteeSkill },
                [2726] = new DoodadFuncFakeUse { Id = 2726, FakeSkillId = MentorSkill }
            },
            [nameof(DoodadFuncLootItem)] = new()
            {
                [3067] = _menteeLoot,
                [3068] = _mentorLoot
            }
        });
        _singletonField.SetValue(null, _manager);
    }

    [After(Test)]
    public void Teardown()
    {
        _singletonField.SetValue(null, _previousManager);
        _skillManagerScope.Dispose();
    }

    [Test]
    public async Task Use_RequiresMentorThenMenteeThenMentor_AndGrantsEachSealToThatActor()
    {
        var chest = CreateChest(MentorActivationPhase);
        var mentor = new Doodad { ObjId = 1 };
        var mentee = new Doodad { ObjId = 2 };

        // Skill-less loot-open and the mentee's skill cannot skip the mentor activation gate.
        chest.Use(mentee, 0);
        chest.Use(mentee, MenteeSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(MentorActivationPhase);
        await Assert.That(_menteeLoot.Recipients).IsEmpty();

        chest.Use(mentor, MentorSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(MenteeGatePhase);

        // The server's skill-less loot-open path cannot claim a seal while the chest awaits a role skill.
        chest.Use(mentor, 0);
        chest.Use(mentor, MentorSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(MenteeGatePhase);
        await Assert.That(_menteeLoot.Recipients).IsEmpty();

        chest.Use(mentee, MenteeSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(MentorGatePhase);
        await Assert.That(_menteeLoot.Recipients).Count().IsEqualTo(1);
        await Assert.That(_menteeLoot.Recipients[0]).IsSameReferenceAs(mentee);
        await Assert.That(_mentorLoot.Recipients).IsEmpty();

        chest.Use(mentee, 0);
        chest.Use(mentee, MenteeSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(MentorGatePhase);
        await Assert.That(_mentorLoot.Recipients).IsEmpty();

        chest.Use(mentor, MentorSkill);
        await Assert.That(chest.FuncGroupId).IsEqualTo(FinalPhase);
        await Assert.That(_mentorLoot.Recipients).Count().IsEqualTo(1);
        await Assert.That(_mentorLoot.Recipients[0]).IsSameReferenceAs(mentor);
    }

    [Test]
    [Arguments(MenteeLootPhase, MenteeGatePhase)]
    [Arguments(MentorLootPhase, MentorGatePhase)]
    [Arguments(MenteeGatePhase, FinalPhase)]
    [Arguments(MentorGatePhase, FinalPhase)]
    public async Task Timer_ContinuesToAuthoredRecoveryPhase(uint currentPhase, uint recoveryPhase)
    {
        var chest = CreateChest(currentPhase);
        var timer = new DoodadFuncTimerTask(null, chest, 0, (int)recoveryPhase);
        chest.FuncTask = timer;

        timer.Execute();

        await Assert.That(chest.FuncGroupId).IsEqualTo(recoveryPhase);
        await Assert.That(chest.FuncTask).IsNull();
        await Assert.That(_menteeLoot.Recipients).IsEmpty();
        await Assert.That(_mentorLoot.Recipients).IsEmpty();
    }

    private static DoodadFunc Func(uint key, uint templateId, string type, uint nextPhase) => new()
    {
        FuncKey = key,
        FuncId = templateId,
        FuncType = type,
        SkillId = 0,
        NextPhase = (int)nextPhase
    };

    private static Doodad CreateChest(uint phase)
    {
        var chest = new Doodad
        {
            Template = new DoodadTemplate
            {
                Id = 7660,
                FuncGroups =
                [
                    new() { Id = MentorActivationPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = MenteeGatePhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = MenteeLootPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = MentorGatePhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = MentorLootPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = FinalPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal }
                ]
            }
        };
        chest.FuncGroupId = phase;
        return chest;
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    private sealed class TrackingLootItem : DoodadFuncLootItem
    {
        public List<BaseUnit> Recipients { get; } = [];

        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            Recipients.Add(caster);
            owner.ToNextPhase = true;
        }
    }
}
