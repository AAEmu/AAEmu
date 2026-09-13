using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class ExpeditionTextRulesTests
{
    [Test]
    public async Task NoticeLimit_IsMeasuredInUtf8Bytes()
    {
        await Assert.That(ExpeditionTextRules.IsValidNotice(new string('Ж', 400))).IsTrue();
        await Assert.That(ExpeditionTextRules.IsValidNotice(new string('Ж', 401))).IsFalse();
    }

    [Test]
    public async Task RoleNameLimit_IsMeasuredInUtf8Bytes()
    {
        await Assert.That(ExpeditionTextRules.IsValidRoleName(new string('Ж', 64))).IsTrue();
        await Assert.That(ExpeditionTextRules.IsValidRoleName(new string('Ж', 65))).IsFalse();
    }

    [Test]
    public async Task TruncateUtf8_PreservesRuneBoundariesAtNativeNoticeLimit()
    {
        var value = new string('Ж', 401);

        var result = ExpeditionTextRules.TruncateUtf8(value, ExpeditionTextRules.MaximumNoticeUtf8Bytes);

        await Assert.That(result).IsEqualTo(new string('Ж', 400));
        await Assert.That(System.Text.Encoding.UTF8.GetByteCount(result)).IsEqualTo(800);
    }
}
