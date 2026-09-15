namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// Trial phases as the client's own <c>X2Trial</c> state table numbers them. The client switches its
/// step ribbon and arms its windows on these exact values: 0 free, 1 the case file is opened,
/// 2 the bench is gathering, 3 the bench reads the record, 4 the defendant's final statement,
/// 5 the bench votes, 6 the trial could not continue so the default sentence is given, 7 the defendant
/// admitted guilt, 8 the trial is over.
/// </summary>
public enum TrialState : byte
{
    Free = 0,
    /// <summary>The court opens the case file.</summary>
    WaitingCrimeRecord = 1,
    /// <summary>The bench is being gathered - the client's own wording is "at most one minute".</summary>
    WaitingJury = 2,
    /// <summary>The bench reads the crime record - "at most ten minutes".</summary>
    Testimony = 3,
    /// <summary>The defendant's final statement - "at most one minute".</summary>
    FinalStatement = 4,
    /// <summary>The bench votes - "at most three minutes".</summary>
    Sentence = 5,
    /// <summary>The trial could not continue; the default sentence is applied.</summary>
    GuiltyBySystem = 6,
    /// <summary>The defendant admitted guilt.</summary>
    GuiltyByUser = 7,
    /// <summary>The trial is over.</summary>
    PostSentence = 8
}

public enum TrialVerdict
{
    Pending,
    Guilty,
    NotGuilty
}

/// <summary>
/// Pure trial rules: what a juror's vote means, when a verdict lands, and where a juror may sit.
/// </summary>
/// <remarks>
/// A juror's vote is the client's own sentence choice, not a number of minutes: the verdict window
/// offers six rows and sends the row's constant, where <see cref="NotGuiltyChoice"/> is not guilty and
/// 2..6 are the five guilty tiers. Reading the byte as "above zero means guilty" would make the first
/// guilty row a not-guilty vote, so the mapping lives here and nowhere else.
/// </remarks>
public static class TrialVerdictRules
{
    /// <summary>The client's SENTENCE_NOT_GUILTY row constant.</summary>
    public const byte NotGuiltyChoice = 1;

    /// <summary>The client's SENTENCE_GUILTY_1 row constant - the first guilty row.</summary>
    public const byte FirstGuiltyChoice = 2;

    /// <summary>The client's SENTENCE_GUILTY_5 row constant - the last guilty row.</summary>
    public const byte LastGuiltyChoice = 6;

    /// <summary>True for the not-guilty row.</summary>
    public static bool IsNotGuiltyChoice(byte choice) => choice == NotGuiltyChoice;

    /// <summary>True for one of the five guilty rows.</summary>
    public static bool IsGuiltyChoice(byte choice) =>
        choice is >= FirstGuiltyChoice and <= LastGuiltyChoice;

    /// <summary>True for a choice the client's verdict window can actually send.</summary>
    public static bool IsValidChoice(byte choice) =>
        choice is >= NotGuiltyChoice and <= LastGuiltyChoice;

    /// <summary>
    /// The guilty row as a 1-based tier (1 = the lightest sentence the window offers), or 0 for a
    /// choice that is not a guilty verdict.
    /// </summary>
    public static int GuiltyTier(byte choice) =>
        IsGuiltyChoice(choice) ? choice - FirstGuiltyChoice + 1 : 0;

    /// <summary>
    /// The byte the ruling packet carries: the not-guilty constant, or the highest guilty tier the
    /// bench chose - the court reads one sentence out of a split vote.
    /// </summary>
    public static byte RulingChoice(TrialVerdict verdict, byte highestGuiltyChoice) =>
        verdict == TrialVerdict.Guilty && IsGuiltyChoice(highestGuiltyChoice)
            ? highestGuiltyChoice
            : NotGuiltyChoice;

    /// <summary>
    /// A verdict lands once every seated juror has voted. With no jurors seated there is nothing to
    /// hear, so the case stays pending rather than auto-convicting anyone. Ties acquit.
    /// </summary>
    public static TrialVerdict Tally(int guiltyVotes, int notGuiltyVotes, int seatedJurors)
    {
        if (seatedJurors <= 0 || guiltyVotes + notGuiltyVotes < seatedJurors)
            return TrialVerdict.Pending;

        return guiltyVotes > notGuiltyVotes ? TrialVerdict.Guilty : TrialVerdict.NotGuilty;
    }

    /// <summary>True for a seat the client's own table can hold.</summary>
    public static bool IsValidSeat(int court, int juryNumber) =>
        court >= 0 && juryNumber is >= 0 and <= 4;
}
