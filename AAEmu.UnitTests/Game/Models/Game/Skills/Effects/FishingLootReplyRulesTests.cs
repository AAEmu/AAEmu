using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public sealed class FishingLootReplyRulesTests
{
    [Test]
    public async Task MissingTargetUsesInvalidTarget()
    {
        await Assert.That(FishingLootReplyRules.Reply(false, true, true, false))
            .IsEqualTo(ErrorMessageType.InvalidTarget);
    }

    [Test]
    public async Task MissingOrEmptyPackUsesInvalid()
    {
        await Assert.That(FishingLootReplyRules.Reply(true, false, false, false))
            .IsEqualTo(ErrorMessageType.Invalid);
        await Assert.That(FishingLootReplyRules.Reply(true, true, false, false))
            .IsEqualTo(ErrorMessageType.Invalid);
    }

    [Test]
    public async Task RefusedDeliveryUsesBagFull()
    {
        await Assert.That(FishingLootReplyRules.Reply(true, true, true, false))
            .IsEqualTo(ErrorMessageType.BagFull);
    }

    [Test]
    public async Task SuccessfulDeliveryHasNoErrorReply()
    {
        await Assert.That(FishingLootReplyRules.Reply(true, true, true, true)).IsNull();
    }
}
