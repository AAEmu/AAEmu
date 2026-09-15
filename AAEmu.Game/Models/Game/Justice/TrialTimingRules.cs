using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// The clocks a trial runs on. Every one of them is shipped data - <c>content_configs</c> rows
/// <c>jury_wait_time</c>, <c>testimony_wait_time</c>, <c>final_testimony_time</c> and
/// <c>sentence_wait_time</c> (60 / 90 / 60 / 60 seconds as shipped) - because the client prints the
/// same limit in its step messages, so the server's clock and the client's narration agree. The
/// environment overrides stay for smoke runs.
/// </summary>
public static class TrialTimingRules
{
    /// <summary>How long the bench is gathered before the trial starts with the jurors who came.</summary>
    public static int JuryGatherSeconds => Read("AAEMU_TRIAL_GATHER_SECONDS", "jury_wait_time", 60);

    /// <summary>The record-reading window the bench gets before the defendant speaks.</summary>
    public static int TestimonySeconds => Read("AAEMU_TRIAL_TESTIMONY_SECONDS", "testimony_wait_time", 90);

    /// <summary>The defendant's final statement.</summary>
    public static int FinalStatementSeconds => Read("AAEMU_TRIAL_STATEMENT_SECONDS", "final_testimony_time", 60);

    /// <summary>The bench's vote window. Every seat voting closes it early; the clock backstops it.</summary>
    public static int VoteSeconds => Read("AAEMU_TRIAL_VOTE_SECONDS", "sentence_wait_time", 60);

    /// <summary>
    /// The court UI's clocks (the phase countdown, the wait dialog's penalty line) are milliseconds.
    /// A raw seconds value renders as a thousandth of its face value, so every time we hand the
    /// client is converted here.
    /// </summary>
    public static uint ToClientMilliseconds(int seconds) => (uint)Math.Max(0, seconds) * 1000u;

    /// <summary>Minutes read from shipped data arrive unsigned; they cannot be negative.</summary>
    public static uint ToClientMilliseconds(uint seconds) => seconds * 1000u;

    private static int Read(string environmentName, string configName, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName);
        if (int.TryParse(raw, out var value) && value >= 0)
            return value;

        return ContentConfigGameData.Instance.GetInt(configName, fallback);
    }
}
