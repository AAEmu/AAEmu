using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

[NotInParallel]
public class DoodadGuardTimeTests
{
    private static readonly DateTime s_plantTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, false)]
    [Arguments(1, true)]
    public async Task IsPublicPropertyByAge_AtExpiryBoundary_BecomesPublicAfterDeadline(int ticksFromExpiry, bool expected)
    {
        var doodad = CreateDoodad(3600, s_plantTime);
        var now = s_plantTime.AddHours(1).AddTicks(ticksFromExpiry);

        await Assert.That(doodad.IsPublicPropertyByAge(now)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(3600u, 1800, true)]
    [Arguments(3600u, 7200, false)]
    [Arguments(172_800u, 129600, true)]
    [Arguments(0u, 172800, true)]
    public async Task Use_OtherPlayersDoodad_GeneratesEvidenceOnlyWhileProtected(uint guardTime, int ageSeconds, bool expectEvidence)
    {
        var doodadManager = new DoodadManager(
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object);
        // A non-loot function lets Use reach its theft decision without executing a gameplay effect.
        SetField(doodadManager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>
        {
            [0] = [new DoodadFunc { SkillId = 0, FuncType = "TestNoOp" }]
        });
        var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_skills", new Dictionary<uint, SkillTemplate>());
        var doodadInstanceField = GetField(typeof(Singleton<DoodadManager>), "s_instance", BindingFlags.Static);
        var skillInstanceField = GetField(typeof(Singleton<SkillManager>), "s_instance", BindingFlags.Static);
        var previousDoodadManager = doodadInstanceField.GetValue(null);
        var previousSkillManager = skillInstanceField.GetValue(null);
        try
        {
            doodadInstanceField.SetValue(null, doodadManager);
            skillInstanceField.SetValue(null, skillManager);
            var criminal = new CharacterMock { Id = 99 };
            var doodad = CreateDoodad(guardTime, DateTime.UtcNow.AddSeconds(-ageSeconds));

            doodad.Use(criminal);

            await Assert.That(doodad.EvidenceRequests.Count).IsEqualTo(expectEvidence ? 1 : 0);
            if (expectEvidence)
                await Assert.That(doodad.EvidenceRequests[0]).IsSameReferenceAs(criminal);
        }
        finally
        {
            skillInstanceField.SetValue(null, previousSkillManager);
            doodadInstanceField.SetValue(null, previousDoodadManager);
        }
    }

    private static RecordingDoodad CreateDoodad(uint guardTime, DateTime plantTime)
    {
        return new RecordingDoodad
        {
            PlantTime = plantTime,
            OwnerId = 42,
            OwnerType = DoodadOwnerType.Character,
            Template = new DoodadTemplate { Group = new DoodadGroups { GuardOnFieldTime = guardTime } }
        };
    }

    private static void SetField(object target, string name, object value)
    {
        GetField(target.GetType(), name, BindingFlags.Instance).SetValue(target, value);
    }

    private static FieldInfo GetField(Type type, string name, BindingFlags scope)
    {
        return type.GetField(name, scope | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name);
    }

    private sealed class RecordingDoodad : Doodad
    {
        public List<Character> EvidenceRequests { get; } = [];

        internal override Doodad GenerateTheftEvidence(Character criminal)
        {
            EvidenceRequests.Add(criminal);
            return null;
        }
    }
}
