using System.Linq;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public QuestTemplate ParentQuestTemplate { get; set; }
    public QuestComponent ParentComponent { get; set; }

    /// <summary>
    /// quest_act_xxx Id
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// Current Objective Count for this Act
    /// </summary>
    public int Objective { get; set; }
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

    /// <summary>
    /// Called for every QuestAct in a component when the component is activated
    /// </summary>
    public virtual void Initialize()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct started {Id}.");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed
    /// </summary>
    public virtual void Completed()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct completed {Id}.");
    }

    /// <summary>
    /// Checks is this specific QuestAct is completed (checks objectives)
    /// </summary>
    /// <returns></returns>
    public virtual bool IsCompleted()
    {
        return Objective >= Count;
    }

    /// <summary>
    /// Checks if the current object count is enough to trigger a early complete.
    /// </summary>
    /// <returns>Only returns true, if ObjectiveEarlyCompleteCount is set, and ObjectiveEarlyCompleteCount &lt;= Objective &lt; ObjectiveCount</returns>
    public virtual bool IsEarlyCompletable()
    {
        return (ObjectiveEarlyCompleteCount > 0) && (Objective >= ObjectiveEarlyCompleteCount) && (Objective < Count);
    }

    /// <summary>
    /// Checks if the objective is more than the normal amount, but less than the overachieve threshold
    /// </summary>
    /// <returns>Only returns true if ObjectiveOverachieveCount is set, and ObjectiveCount &lt; Objective &lt; ObjectiveOverachieveCount</returns>
    public virtual bool IsExtraProgress()
    {
        return (ObjectiveOverAchieveCount > 0) && (Objective > Count) && (Objective < ObjectiveOverAchieveCount);
    }

    /// <summary>
    /// Checks if the objective is enough to fully overachieve
    /// </summary>
    /// <returns>Only returns true if ObjectiveOverachieveCount is set, and ObjectiveCount &lt; Objective &lt; ObjectiveOverachieveCount</returns>
    public virtual bool IsOverAchieved()
    {
        return (ObjectiveOverAchieveCount > 0) && (Objective >= ObjectiveOverAchieveCount);
    }

    /// <summary>
    /// Moves objective for this QuestAct one further
    /// </summary>
    public virtual void Update(int updateAmount = 1)
    {
        if (updateAmount == 0)
            return;
        Objective += updateAmount;
        Logger.Info($"{QuestActTemplateName} - QuestAct {Id} has been updated by {updateAmount} for a total of {Objective}.");
    }

    /// <summary>
    /// Resets objective for this QuestAct
    /// </summary>
    public virtual void ClearStatus()
    {
        Logger.Info($"{QuestActTemplateName} - Reset QuestAct {Id} objectives.");
        Objective = 0;
    }

    public virtual bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Info($"{QuestActTemplateName} - Use QuestAct {Id}, Character: {character.Name}, Objective {objective}.");
        return false;
    }
}
