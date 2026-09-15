using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestTemplate : IQuestTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Repeatable { get; set; }
    public byte Level { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public byte RaceMask { get; set; } = byte.MaxValue;
    public bool Selective { get; set; }
    public bool Successive { get; set; }
    public bool RestartOnFail { get; set; }
    public uint ChapterIdx { get; set; }
    public uint QuestIdx { get; set; }
    public uint MilestoneId { get; set; }
    public bool LetItDone { get; set; }
    public QuestDetail DetailId { get; set; }
    public uint ZoneId { get; set; }
    public uint CategoryId { get; set; }
    public int Degree { get; set; }
    public bool UseQuestCamera { get; set; }
    public int Score { get; set; }
    public bool UseAcceptMessage { get; set; }
    public bool UseCompleteMessage { get; set; }
    public uint GradeId { get; set; }
    public bool Translate { get; set; }
    public int Priority { get; set; }
    public bool OnlyOneScoreTitle { get; set; }
    public bool HideChapterIndex { get; set; }
    public IDictionary<uint, QuestComponentTemplate> Components { get; set; } = new Dictionary<uint, QuestComponentTemplate>();

    /// <summary>
    /// The level half of <see cref="MeetsContextRequirements"/>, so a refusal can name the level gate
    /// on the client's own level row instead of the generic requirement row.
    /// </summary>
    public bool MeetsLevelRequirements(Character character)
    {
        if (character == null)
            return false;

        return character.Level >= MinLevel && (MaxLevel == 0 || character.Level <= MaxLevel);
    }

    public bool MeetsContextRequirements(Character character)
    {
        if (!MeetsLevelRequirements(character))
            return false;

        if (RaceMask == byte.MaxValue)
            return true;

        var race = (int)character.Race;
        if (race <= (int)Race.None || race > sizeof(byte) * 8)
            return false;

        var raceFlag = 1 << (race - 1);
        return (RaceMask & raceFlag) != 0;
    }

    public QuestComponentTemplate GetFirstComponent(QuestComponentKind step)
    {
        return Components.Values
                .FirstOrDefault(cp => cp.KindId == step);
    }
    public QuestComponentTemplate[] GetComponents(QuestComponentKind step)
    {
        return Components.Values
                .Where(cp => cp.KindId == step)
                .ToArray();
    }
    public QuestComponentTemplate[] GetComponents(uint componentId)
    {
        return Components.Values
                .Where(cp => cp.Id == componentId)
                .ToArray();
    }
}
