using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using System.Reflection;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class ExpeditionRecruitmentPolicyTests
{
    [Test]
    [Arguments((short)0)]
    [Arguments((short)1)]
    [Arguments((short)0x3f)]
    public async Task InterestMask_AcceptsOnlySixClientBits(short mask) =>
        await Assert.That(ExpeditionRecruitmentPolicy.IsValidInterestMask(mask)).IsTrue();

    [Test]
    [Arguments((short)0x40)]
    [Arguments((short)-1)]
    public async Task InterestMask_RejectsUnknownBits(short mask) =>
        await Assert.That(ExpeditionRecruitmentPolicy.IsValidInterestMask(mask)).IsFalse();

    [Test]
    public async Task CostsAndApplicationLimit_UseLoadedContentValues()
    {
        var manager = new ExpeditionManager(Mock.Of<IExpeditionIdManager>().Object, Mock.Of<ITeamManager>().Object,
            Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object);
        var field = typeof(ExpeditionManager).GetField("_contentConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var values = (Dictionary<string, long>)field.GetValue(manager)!;
        values["expedition_recruit_period_min"] = 4;
        values["expedition_recruit_period_min_cost"] = 12345;
        values["expedition_recruit_period_max"] = 11;
        values["expedition_recruit_period_max_cost"] = 67890;
        values["expedition_recruit_apply_max"] = 7;

        await Assert.That(ExpeditionRecruitmentPolicy.TryGetCost(manager, 4, out var shortCost)).IsTrue();
        await Assert.That(shortCost).IsEqualTo(12345);
        await Assert.That(ExpeditionRecruitmentPolicy.TryGetCost(manager, 11, out var longCost)).IsTrue();
        await Assert.That(longCost).IsEqualTo(67890);
        await Assert.That(ExpeditionRecruitmentPolicy.TryGetCost(manager, 3, out _)).IsFalse();
        await Assert.That(ExpeditionRecruitmentPolicy.MaximumApplications(manager)).IsEqualTo(7);
    }

}
