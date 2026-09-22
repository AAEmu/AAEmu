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
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

[NotInParallel]
public sealed class DoodadFuncSpawnUseTests
{
    private const uint SkillId = 37997;
    private const uint StartPhase = 36667;
    private const uint NextPhase = 36937;

    private FieldInfo _singletonField;
    private object _previousManager;
    private SingletonScope<SkillManager> _skillManagerScope;

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
        SetField(manager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [StartPhase] =
            [
                new DoodadFunc
                {
                    GroupId = StartPhase,
                    FuncId = 5,
                    FuncType = nameof(DoodadFuncSpawn),
                    SkillId = SkillId,
                    NextPhase = (int)NextPhase
                }
            ]
        });
        SetField(manager, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        SetField(manager, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncSpawn)] = new()
            {
                [5] = new DoodadFuncSpawn { Id = 5, OwnerTypeId = BaseUnitType.Slave }
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
    public async Task Use_DoesNotAdvancePhaseWhenSpawnOwnerTypeIsUnsupported()
    {
        var owner = new Doodad
        {
            Template = new DoodadTemplate
            {
                Id = 12664,
                FuncGroups =
                [
                    new() { Id = StartPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal },
                    new() { Id = NextPhase, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal }
                ]
            }
        };
        owner.FuncGroupId = StartPhase;

        owner.Use(new Doodad { ObjId = 1 }, SkillId);

        await Assert.That(owner.FuncGroupId).IsEqualTo(StartPhase);
        await Assert.That(owner.ToNextPhase).IsFalse();
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
}
