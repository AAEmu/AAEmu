namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// A character's bot-check state: how many checks are still to be answered, how many answers have been
/// wrong, and whether the character is under suspicion. The client reads the two counters with the
/// character state and learns about the suspicion from the bot-trial packet.
/// </summary>
/// <remarks>
/// The counters are session state rather than a persisted row: nothing in the client says they outlive a
/// session, and the suspicion is the part the client actually shows. The bot-trial effect is what marks
/// the suspicion today; the checks are granted by the quiz flow, whose question packet is not identified
/// yet, so nothing grants them until it is.
/// </remarks>
public class CharacterBotCheck
{
    /// <summary>Checks left to answer; the client draws this beside the quiz.</summary>
    public byte RemainChecks { get; private set; }

    /// <summary>Wrong answers accumulated so far.</summary>
    public short FailedAnswers { get; private set; }

    /// <summary>Whether the character has been flagged for a bot trial.</summary>
    public bool OnTrial { get; private set; }

    /// <summary>Flags the character for a bot trial, which is what the bot-trial effect does.</summary>
    public bool MarkOnTrial()
    {
        if (OnTrial)
            return false;

        OnTrial = true;
        return true;
    }

    /// <summary>
    /// Spends one check on an answer. Whether the answer was right cannot be decided here: the question it
    /// answers is not identified yet, so nothing counts failures until it is.
    /// </summary>
    public bool RecordAnswer()
    {
        if (RemainChecks == 0)
            return false;

        RemainChecks--;
        return true;
    }
}
