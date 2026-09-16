using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// CancelOngoingBuff (type 61): value1 is a buff tag and value2 a buff id, one or the other. The 995 shipped
/// rows are unreachable from content, so the mapping is all there is to pin.
/// </summary>
public class OngoingBuffCancelRulesTests
{
    [Test]
    public async Task TagRow_ReadsTheTagSlot()
    {
        var request = OngoingBuffCancelRules.Resolve(695, 0);

        await Assert.That(request.BuffTagId).IsEqualTo(695u);
        await Assert.That(request.BuffId).IsEqualTo(0u);
        await Assert.That(request.IsNoOp).IsFalse();
    }

    [Test]
    public async Task IdRow_ReadsTheIdSlot()
    {
        var request = OngoingBuffCancelRules.Resolve(0, 50153);

        await Assert.That(request.BuffTagId).IsEqualTo(0u);
        await Assert.That(request.BuffId).IsEqualTo(50153u);
        await Assert.That(request.IsNoOp).IsFalse();
    }

    [Test]
    public async Task EmptyRow_IsANoOp()
    {
        // 977 of the 995 rows carry nothing at all.
        await Assert.That(OngoingBuffCancelRules.Resolve(0, 0).IsNoOp).IsTrue();
    }

    [Test]
    public async Task NegativeSlots_AreNotIds()
    {
        // No shipped row is negative; the clamp only keeps a malformed row from wrapping into a huge id.
        var request = OngoingBuffCancelRules.Resolve(-5, -1);

        await Assert.That(request.BuffTagId).IsEqualTo(0u);
        await Assert.That(request.BuffId).IsEqualTo(0u);
        await Assert.That(request.IsNoOp).IsTrue();
    }
}
