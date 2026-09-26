using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Pins that the catalog's no-link sentinel is reported as NO link rather than as a
/// resolved slot. The sentinel row exists so an absent link still has a value to point
/// at; the row resolving successfully is a property of the catalog, not evidence that
/// the skill equips anything.
/// </summary>
[NotInParallel]
public class SkillManagerEquipLinkTests
{
    private const int NoLinkId = -1;
    private const int LinkedSlotId = 5;
    private const uint SkillWithNoLink = 900;
    private const uint SkillWithLink = 901;
    private const uint SkillWithUnknownLink = 902;

    private IDisposable _scope;

    [Before(Test)]
    public void SeedManager()
    {
        _scope = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        var manager = SkillManager.Instance;
        manager.SetEquipSlotDefinitionsForTest(
        [
            (NoLinkId, SkillEquipSlotCatalog.NoLinkSlotName, null),
            (LinkedSlotId, "hands", "armor")
        ]);

        manager.SetSkillForTest(new SkillTemplate { Id = SkillWithNoLink, LinkEquipSlotId = NoLinkId });
        manager.SetSkillForTest(new SkillTemplate { Id = SkillWithLink, LinkEquipSlotId = LinkedSlotId });
        manager.SetSkillForTest(new SkillTemplate { Id = SkillWithUnknownLink, LinkEquipSlotId = 4242 });
    }

    [After(Test)]
    public void TearDownManager() => _scope?.Dispose();

    [Test]
    public async Task ASkillPointingAtTheNoLinkSentinelHasNoLink()
    {
        var resolved = SkillManager.Instance.TryGetLinkedEquipSlot(SkillWithNoLink, out var definition);

        await Assert.That(resolved).IsFalse();
        await Assert.That(definition).IsNull();
    }

    [Test]
    public async Task ARealSlotStillResolves()
    {
        var resolved = SkillManager.Instance.TryGetLinkedEquipSlot(SkillWithLink, out var definition);

        await Assert.That(resolved).IsTrue();
        await Assert.That(definition.Name).IsEqualTo("hands");
        await Assert.That(definition.Id).IsEqualTo(LinkedSlotId);
    }

    [Test]
    public async Task AnUnknownSlotIdStillReportsNoLink()
    {
        var resolved = SkillManager.Instance.TryGetLinkedEquipSlot(SkillWithUnknownLink, out _);

        await Assert.That(resolved).IsFalse();
    }

    [Test]
    public async Task AnUnloadedSkillReportsNoLink()
    {
        var resolved = SkillManager.Instance.TryGetLinkedEquipSlot(999999, out _);

        await Assert.That(resolved).IsFalse();
    }
}
