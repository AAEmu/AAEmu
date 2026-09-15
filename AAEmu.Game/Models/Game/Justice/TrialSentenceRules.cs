using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// What a guilty row of the court's verdict window actually sentences the defendant to.
/// </summary>
/// <remarks>
/// The five guilty rows are not fixed lengths: the client builds them as percentages of the case's base
/// sentence (the millisecond clock the court sends in the wait status and the crime sheet), and the
/// percentages are shipped data - <c>content_configs</c> rows
/// <c>trial_sentence_ratio_range_1</c>..<c>_5</c>, 50/80/100/130/150 as shipped. The court therefore
/// reads a row as a share of the base, not as a number of minutes.
/// </remarks>
public static class TrialSentenceRules
{
    private static readonly string[] RatioConfigNames =
    [
        "trial_sentence_ratio_range_1",
        "trial_sentence_ratio_range_2",
        "trial_sentence_ratio_range_3",
        "trial_sentence_ratio_range_4",
        "trial_sentence_ratio_range_5"
    ];

    /// <summary>The share of the base sentence a row means when its config row is missing (the middle row).</summary>
    public const int DefaultRatioPercent = 100;

    /// <summary>
    /// The share of the base sentence behind a guilty row, in percent: row 2 (the lightest) through
    /// row 6 (the heaviest). A choice that is not a guilty row has no share.
    /// </summary>
    public static int RatioPercent(byte choice)
    {
        var tier = TrialVerdictRules.GuiltyTier(choice);
        if (tier <= 0 || tier > RatioConfigNames.Length)
            return 0;

        return ContentConfigGameData.Instance.GetInt(RatioConfigNames[tier - 1], DefaultRatioPercent);
    }

    /// <summary>
    /// The sentence a guilty row carries, in milliseconds, computed the way the client computes its
    /// own rows: the base clock times the row's share, in whole milliseconds.
    /// </summary>
    public static uint SentenceMilliseconds(uint baseMilliseconds, byte choice)
    {
        var percent = RatioPercent(choice);
        if (percent <= 0)
            return baseMilliseconds;

        return (uint)((long)baseMilliseconds * percent / 100L);
    }

    /// <summary>
    /// The same sentence in whole minutes, rounded down as the client's own display is.
    /// </summary>
    public static int SentenceMinutes(uint baseMilliseconds, byte choice) =>
        (int)(SentenceMilliseconds(baseMilliseconds, choice) / 60000u);

    /// <summary>
    /// The guilty row that carries exactly the base sentence. That is the sentence the defendant is
    /// warned about when he gives up, and the "default sentence" the court imposes when the trial
    /// cannot continue - so both of those read this row rather than a row number written down here.
    /// </summary>
    public static byte BaseSentenceChoice()
    {
        for (var choice = TrialVerdictRules.FirstGuiltyChoice;
             choice <= TrialVerdictRules.LastGuiltyChoice;
             choice++)
        {
            if (RatioPercent(choice) == DefaultRatioPercent)
                return choice;
        }

        return TrialVerdictRules.FirstGuiltyChoice;
    }
}
