namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How long a bubble effect's line is given before the effect that showed it is done with its read
/// window. Pure timing decision, kept out of <see cref="Effects.BubbleEffect"/> so it can be asserted
/// without a live world; the effect only hands the result to the task scheduler.
/// </summary>
public static class BubbleReadTimeRules
{
    /// <summary>
    /// Reading speed source, as cited by the original effect:
    /// https://iovs.arvojournals.org/article.aspx?articleid=2166061
    /// A 2012 study put the average adult English reading speed at 228±30 words, 313±38 syllables and
    /// 987±118 characters per minute; the effect took the low end of 900 characters per minute, which
    /// is where this 0.015 per character comes from. Shipped bubble lines are short enough that the
    /// <see cref="MinimumReadTimeMilliseconds"/> floor is what they actually resolve to (the longest
    /// localized line is 264 characters).
    /// </summary>
    public const double PerCharacter = 0.015;

    /// <summary>Bubble with no localized line to read: hold it for 2.5 seconds.</summary>
    public const int NoTextReadTimeMilliseconds = 2500;

    /// <summary>Shortest read window, so even one- and two-character bubbles register: 1.25 seconds.</summary>
    public const int MinimumReadTimeMilliseconds = 1250;

    /// <summary>
    /// Read window for a localized bubble line, in milliseconds. Every value this returns is the
    /// scheduled delay of the bubble's read window, never a blocking wait.
    /// </summary>
    public static int GetReadTimeMilliseconds(string localizedBubbleText)
    {
        if (string.IsNullOrEmpty(localizedBubbleText))
            return NoTextReadTimeMilliseconds;

        var readTime = (int)Math.Round(localizedBubbleText.Length * PerCharacter);
        return Math.Max(readTime, MinimumReadTimeMilliseconds);
    }
}
