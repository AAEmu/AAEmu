namespace AAEmu.Game.Models.Game.Achievement;

/// <summary>
/// One objective of an achievement: the record whose value counts towards it.
/// </summary>
/// <param name="ObjectiveId">The <c>achievement_objectives</c> row.</param>
/// <param name="RecordId">The <c>char_records</c> counter this objective watches.</param>
public readonly record struct AchievementObjective(uint ObjectiveId, uint RecordId);

/// <summary>How far a character has got with one achievement.</summary>
/// <param name="Progress">What to report as the character's amount for the achievement.</param>
/// <param name="Required">The amount the achievement asks for.</param>
/// <param name="Satisfied">How many of its objectives have been touched at all.</param>
/// <param name="Complete">Whether the amount has been reached.</param>
public readonly record struct AchievementEvaluation(int Progress, int Required, int Satisfied, bool Complete);

/// <summary>
/// What an achievement's <c>complete_num</c> and <c>complete_or</c> mean, and when it is complete.
/// </summary>
/// <remarks>
/// <para>
/// Neither number is documented in the content, so this reading is taken from the shipped data and from the
/// achievement text that describes it. <c>complete_or</c> picks one of two shapes, and the content keeps to
/// each shape exactly:
/// </para>
/// <list type="bullet">
/// <item><b><c>complete_or</c> true — the number counts objectives.</b> Not one of the multi-objective rows
/// with this flag asks for more objectives than it has. The text says the same: a house achievement lists 116
/// designs and one objective is enough ("complete <i>one</i> of them"), the novel-crafting ladder lists 45
/// titles and asks for 5, 10, 20, 30, then "all 45 kinds".</item>
/// <item><b><c>complete_or</c> false — the number is a total the objectives add into.</b> 361 of these rows
/// ask for more than they have objectives at all: 1,200 quests split over 11 records, 10,000 livestock over
/// three, 10,000 kills over nine. Their objectives are alternatives rather than a checklist — 168 candidate
/// armor pieces stand behind "awaken a master's armor and obtain <i>an</i> Eferium armor".</item>
/// <item><b><c>complete_num</c> 0 means all of them</b> under either flag: "train all fourteen heroes"
/// (fourteen child achievements), "complete every Great Explorer achievement" (twenty).</item>
/// </list>
/// <para>
/// An objective counts as satisfied as soon as its record has been touched, which is what the counting shape
/// asks ("craft 5 <i>kinds</i>", "obtain 10 <i>kinds</i>"); the summing shape reads the record's value itself.
/// The amount is clamped to what the achievement asks for because it is what the client is sent and it draws a
/// bar from it. This is the one reading in the slice a live client can still contradict — a completed
/// achievement should show its counter full — and it is kept in this one place so that it is cheap to change.
/// </para>
/// </remarks>
public static class AchievementRules
{
    /// <summary>Evaluates an achievement against the character's record values.</summary>
    /// <param name="completeNum">The achievement's <c>complete_num</c>.</param>
    /// <param name="completeOr">
    /// The achievement's <c>complete_or</c>: true when <paramref name="completeNum"/> counts objectives, false
    /// when it is a total of their record values.
    /// </param>
    /// <param name="objectives">Its objectives, in table order.</param>
    /// <param name="recordValue">Reads a counted record's value for the character.</param>
    public static AchievementEvaluation Evaluate(int completeNum, bool completeOr,
        IReadOnlyList<AchievementObjective> objectives, Func<uint, int> recordValue)
    {
        objectives ??= [];
        recordValue ??= _ => 0;

        // 78 achievements have no objectives at all; nothing counts towards them, so nothing can complete
        // them either. (They are the parents whose children carry the objectives.)
        if (objectives.Count == 0)
            return new AchievementEvaluation(0, completeNum > 0 ? completeNum : 0, 0, false);

        var allOfThem = completeNum <= 0;
        var required = allOfThem ? objectives.Count : completeNum;

        var total = 0;
        var satisfied = 0;
        // Duplicate objectives exist in the content (the same record listed twice); each row is its own
        // objective, so they are counted separately.
        foreach (var objective in objectives)
        {
            var value = recordValue(objective.RecordId);
            if (value <= 0)
                continue;

            satisfied++;
            // Counting objectives: each one is worth one however far past it the record is. Summing them:
            // the records' own values are what the total is made of. "All of them" counts each once either way.
            total += completeOr || allOfThem ? 1 : value;
        }

        var complete = total >= required;
        return new AchievementEvaluation(Math.Min(total, required), required, satisfied, complete);
    }
}
