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
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

[NotInParallel]
public sealed class DoodadFuncFakeUseTests
{
    private const uint InteractionSkill = 11305;
    private const uint StartPhase = 5092;
    private const uint NextPhase = 5093;

    private FieldInfo _singletonField;
    private object _previousManager;
    private SingletonScope<SkillManager> _skillManagerScope;
    private DoodadFunc _function;

    [Before(Test)]
    public void Setup()
    {
        _singletonField = typeof(Singleton<DoodadManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousManager = _singletonField.GetValue(null);
        _skillManagerScope = new SingletonScope<SkillManager>(new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object));

        var manager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);
        _function = new DoodadFunc
        {
            FuncKey = 4352,
            GroupId = StartPhase,
            FuncId = 766,
            FuncType = nameof(DoodadFuncFakeUse),
            SkillId = InteractionSkill,
            NextPhase = (int)NextPhase
        };
        SetField(manager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [StartPhase] = [_function]
        });
        SetField(manager, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        SetField(manager, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncFakeUse)] = new()
            {
                [766] = new DoodadFuncFakeUse { Id = 766, FakeSkillId = 13549 }
            }
        });
        _singletonField.SetValue(null, manager);
    }

    [After(Test)]
    public void Teardown()
    {
        _singletonField.SetValue(null, _previousManager);
        _skillManagerScope.Dispose();
    }

    [Test]
    public async Task Use_RowSkillAdvancesFakeUseWithDifferentTemplateFakeSkill()
    {
        var owner = CreateDoodad();

        owner.Use(new Doodad { ObjId = 1 }, InteractionSkill);

        await Assert.That(owner.FuncGroupId).IsEqualTo(NextPhase);
    }

    [Test]
    public async Task Use_UnrelatedPositiveSkillDoesNotAdvanceRow()
    {
        var owner = CreateDoodad();

        owner.Use(new Doodad { ObjId = 1 }, InteractionSkill + 1);

        await Assert.That(owner.FuncGroupId).IsEqualTo(StartPhase);
    }

    [Test]
    public async Task RowFallback_DoesNotAdvanceForNullCaster()
    {
        var owner = CreateDoodad();

        _function.Use(null, owner, InteractionSkill, (int)NextPhase);

        await Assert.That(owner.ToNextPhase).IsFalse();
    }

    [Test]
    public async Task RowFallback_PreservesSkillLessSubstitution()
    {
        var owner = CreateDoodad();

        _function.Use(new Doodad { ObjId = 1 }, owner, 0, (int)NextPhase);

        await Assert.That(owner.ToNextPhase).IsTrue();
    }

    private static Doodad CreateDoodad()
    {
        var owner = new Doodad
        {
            Template = new DoodadTemplate
            {
                Id = 2432,
                FuncGroups =
                [
                    new() { Id = StartPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = NextPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal }
                ]
            }
        };
        owner.FuncGroupId = StartPhase;
        return owner;
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
}
