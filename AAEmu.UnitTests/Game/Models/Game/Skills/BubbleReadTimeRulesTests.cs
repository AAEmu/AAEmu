using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The read time a bubble effect is held for: an estimate from the localized line, a fallback when
/// there is no line, and a floor so short lines still register. These are the values BubbleEffect used
/// inline before; the effect now schedules the window instead of sleeping it out.
/// </summary>
public class BubbleReadTimeRulesTests
{
    [Test]
    public async Task NoLocalizedLine_UsesTheFallbackWindow()
    {
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(string.Empty))
            .IsEqualTo(BubbleReadTimeRules.NoTextReadTimeMilliseconds);
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(null))
            .IsEqualTo(BubbleReadTimeRules.NoTextReadTimeMilliseconds);
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(string.Empty)).IsEqualTo(2500);
    }

    [Test]
    public async Task AnyShippedLengthLine_IsFlooredAtTheMinimumWindow()
    {
        // The longest localized bubble line in 10.0.2 is 264 characters and the 0.015 per character
        // estimate scores even that at 4 ms, so every real bubble resolves to the 1250 ms floor.
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds("Hi"))
            .IsEqualTo(BubbleReadTimeRules.MinimumReadTimeMilliseconds);
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(new string('x', 264)))
            .IsEqualTo(BubbleReadTimeRules.MinimumReadTimeMilliseconds);
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(new string('x', 83_000)))
            .IsEqualTo(BubbleReadTimeRules.MinimumReadTimeMilliseconds);
    }

    [Test]
    public async Task LongerLine_ScalesWithTheCharacterCount()
    {
        // 90 000 * 0.015 = 1350 ms, past the floor.
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(new string('x', 90_000))).IsEqualTo(1350);
        await Assert.That(BubbleReadTimeRules.GetReadTimeMilliseconds(new string('x', 100_000))).IsEqualTo(1500);
    }
}
