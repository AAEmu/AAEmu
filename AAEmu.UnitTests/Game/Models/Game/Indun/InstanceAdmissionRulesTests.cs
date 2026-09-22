using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public sealed class InstanceAdmissionRulesTests
{
    [Test]
    public async Task EmptySchedule_IsAlwaysOpen()
    {
        await Assert.That(InstanceAdmissionRules.IsOpen([], new DateTime(2026, 9, 20, 12, 0, 0)))
            .IsTrue();
    }

    [Test]
    public async Task DailyZeroWindow_CoversTheWholeNamedDay()
    {
        var windows = new[] { new InstanceEntranceTime(1, 0, 0, 0, 0) };

        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 21, 23, 59, 59))).IsTrue();
        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 22, 0, 0, 0))).IsFalse();
    }

    [Test]
    public async Task ExtendedHourWindow_ContinuesIntoFollowingDay()
    {
        var windows = new[] { new InstanceEntranceTime(1, 20, 0, 28, 0) };

        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 22, 3, 59, 59))).IsTrue();
        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 22, 4, 0, 0))).IsFalse();
    }

    [Test]
    public async Task Schedule_UsesInclusiveStartAndExclusiveEnd()
    {
        var windows = new[] { new InstanceEntranceTime(0, 15, 15, 16, 0) };

        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 20, 15, 15, 0))).IsTrue();
        await Assert.That(InstanceAdmissionRules.IsOpen(
            windows, new DateTime(2026, 9, 20, 16, 0, 0))).IsFalse();
    }

    [Test]
    public async Task BlacklistedTag_IsRejectedWhenPresent()
    {
        var permissions = new[]
        {
            new InstancePermissionTag(InstancePermissionTagKind.Buff, 5947)
        };

        var result = InstanceAdmissionRules.CheckTags(
            permissions, 0, (kind, tag) => kind == InstancePermissionTagKind.Buff && tag == 5947);

        await Assert.That(result).IsEqualTo(InstanceAdmissionFailure.ProhibitedTag);
    }

    [Test]
    public async Task WhitelistedKind_IsNotTreatedAsAnIndunBlacklist()
    {
        var permissions = new[]
        {
            new InstancePermissionTag(InstancePermissionTagKind.Skill, 378),
            new InstancePermissionTag(InstancePermissionTagKind.Skill, 204)
        };
        var whiteListBits = 1u << (int)InstancePermissionTagKind.Skill;

        await Assert.That(InstanceAdmissionRules.CheckTags(
            permissions, whiteListBits, (_, tag) => tag == 204))
            .IsEqualTo(InstanceAdmissionFailure.None);
    }

    [Test]
    public async Task MixedKinds_ApplyTheirOwnWhitelistBits()
    {
        var permissions = new[]
        {
            new InstancePermissionTag(InstancePermissionTagKind.Item, 3428),
            new InstancePermissionTag(InstancePermissionTagKind.Buff, 5947)
        };
        var whiteListBits = 1u << (int)InstancePermissionTagKind.Item;

        var result = InstanceAdmissionRules.CheckTags(
            permissions,
            whiteListBits,
            (kind, _) => kind is InstancePermissionTagKind.Item or InstancePermissionTagKind.Buff);

        await Assert.That(result).IsEqualTo(InstanceAdmissionFailure.ProhibitedTag);
    }
}
