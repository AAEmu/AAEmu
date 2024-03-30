using System.Linq;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate(QuestComponentTemplate parentComponent)
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public QuestTemplate ParentQuestTemplate { get; set; } = parentComponent.ParentQuestTemplate;
    public QuestComponentTemplate ParentComponent { get; set; } = parentComponent;

    /// <summary>
    /// quest_act_xxx Id
    /// </summary>
    public uint Id { get; set; }
    public uint DetailId { get; set; }
    public string DetailType { get; set; }

    /// <summary>
    /// Total Objective Count needed to mark this Act as completed, also used for giving item count, as this is technically also a goal.
    /// </summary>
    public int Count { get; set; } = 0;
    /// <summary>
    /// Minimum Objective Count to be considered early completable
    /// </summary>
    public int ObjectiveEarlyCompleteCount { get; set; } = 0;
    /// <summary>
    /// Maximum Objective Count required for the a full overachieve
    /// </summary>
    public int ObjectiveOverAchieveCount { get; set; } = 0;

    protected string QuestActTemplateName
    {
        get
        {
            return GetType().Name.Split(".").Last();
        }
    }

    public byte ThisComponentObjectiveIndex { get; set; }

    /// <summary>
    /// Called for every QuestAct in a component when the component is activated
    /// </summary>
    public virtual void Initialize(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct started {Id}.");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed or cancelled
    /// </summary>
    public virtual void DeInitialize(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct completed {Id}.");
    }

    /// <summary>
    /// Moves objective for this QuestAct one further
    /// </summary>
    public virtual void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        if (updateAmount == 0)
            return;
        questAct.AddObjective(quest, updateAmount);
        Logger.Info($"{QuestActTemplateName} - QuestAct {Id} has been updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }

    /// <summary>
    /// Resets objective for this QuestAct
    /// </summary>
    public virtual void ClearStatus(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - Reset QuestAct {Id} objectives.");
        questAct.SetObjective(quest, 0);
    }

    public virtual bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Info($"{QuestActTemplateName} - Use QuestAct {Id}, Character: {character.Name}, Objective {objective}.");
        return false;
    }

    public virtual int MaxObjective()
    {
        return ParentQuestTemplate.LetItDone ? Count * 3 / 2 : Count;
    }
}
