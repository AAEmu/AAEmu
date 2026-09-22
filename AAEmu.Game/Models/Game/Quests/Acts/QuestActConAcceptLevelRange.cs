using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Start: the character level must be inside level_min..level_max (QuestAcceptLevelRangeRules).
/// Nine of the ten rows share their Start component with a QuestActConAcceptComponent chain link
/// (quests 10534 to 10542, "reach level N" anniversary quests), where the component's OR lets the
/// chain accept pass; the range is then implied by the previous quest's level objective. Row 2
/// (festival quest 10930, 10..19) is alone and gates the accept by itself.
/// </summary>
public class QuestActConAcceptLevelRange(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {quest.Owner.Level}, Range {LevelMin}..{LevelMax}");
        return QuestAcceptLevelRangeRules.InRange(quest.Owner.Level, LevelMin, LevelMax);
    }
}
