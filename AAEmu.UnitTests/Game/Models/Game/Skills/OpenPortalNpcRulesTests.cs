using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class OpenPortalNpcRulesTests
{
    [Test]
    public async Task AcceptsOnlyContentProvidedNpcPair()
    {
        var effect = new OpenPortalEffect { EnterPortalNpcId = 11, ExitPortalNpcId = 12 };

        await Assert.That(OpenPortalNpcRules.TryResolve(effect, out var enter, out var exit)).IsTrue();
        await Assert.That(enter).IsEqualTo(11u);
        await Assert.That(exit).IsEqualTo(12u);
    }

    [Test]
    public async Task RejectsMissingEitherSide()
    {
        await Assert.That(OpenPortalNpcRules.TryResolve(
            new OpenPortalEffect { EnterPortalNpcId = 11 }, out _, out _)).IsFalse();
        await Assert.That(OpenPortalNpcRules.TryResolve(
            new OpenPortalEffect { ExitPortalNpcId = 12 }, out _, out _)).IsFalse();
        await Assert.That(OpenPortalNpcRules.TryResolve(null, out _, out _)).IsFalse();
        await Assert.That(OpenPortalNpcRules.TryResolve(
            new OpenPortalEffect { EnterPortalNpcId = 11, ExitPortalNpcId = 12, Distance = float.NaN },
            out _, out _)).IsFalse();
        await Assert.That(OpenPortalNpcRules.TryResolve(
            new OpenPortalEffect { EnterPortalNpcId = 11, ExitPortalNpcId = 12, Distance = -1f },
            out _, out _)).IsFalse();
    }
}
