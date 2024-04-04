using System.Linq;
using NLog;

using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate(QuestComponentTemplate parentComponent)
{
    private bool IsInitialized { get; set; }
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public QuestTemplate ParentQuestTemplate { get; set; } = parentComponent.ParentQuestTemplate;
    public QuestComponentTemplate ParentComponent { get; set; } = parentComponent;

    /// <summary>
    /// quest_acts Id, not really used anywhere
    /// </summary>
    public uint ActId { get; set; }

    /// <summary>
    /// quest_act_xxx Id / quest_acts DetailId
    /// </summary>
    public uint DetailId { get; set; }
    public string DetailType { get; set; }

    /// <summary>
    /// Total Objective Count needed to mark this Act as completed, also used for giving item count, as this is technically also a goal.
    /// </summary>
    public int Count { get; set; } = 0;

    protected string QuestActTemplateName
    {
        get
        {
            return GetType().Name.Split(".").Last();
        }
    }

    public byte ThisComponentObjectiveIndex { get; set; } = 0xFF;

    /// <summary>
    /// Called for every QuestAct in a component when the component is activated (Step changed)
    /// </summary>
    public virtual void Initialize(Quest quest, IQuestAct questAct)
    {
        IsInitialized = true;
        Logger.Info($"{QuestActTemplateName} - Initialize {DetailId}.");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed or cancelled (Step changed)
    /// </summary>
    public virtual void DeInitialize(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - DeInitialize {DetailId}.");
        IsInitialized = false;
    }

    /// <summary>
    /// Moves objective for this QuestAct one further
    /// </summary>
    public virtual void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        if (updateAmount == 0)
            return;
        questAct.AddObjective(quest, updateAmount);
        Logger.Info($"{QuestActTemplateName} - {DetailId} has been updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }

    /// <summary>
    /// Resets objective for this QuestAct
    /// </summary>
    public virtual void ClearStatus(Quest quest, IQuestAct questAct)
    {
        Logger.Info($"{QuestActTemplateName} - Reset QuestAct {DetailId} objectives.");
        questAct.SetObjective(quest, 0);
    }

    public virtual bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Info($"{QuestActTemplateName} - Use QuestAct {DetailId}, Character: {character.Name}, Objective {objective}.");
        return false;
    }

    /// <summary>
    /// Execute and check a Act for it's results, called after updating objective counts
    /// </summary>
    /// <param name="quest">Quest this RunAct is called for</param>
    /// <param name="currentObjectiveCount">Current Objective Count</param>
    /// <returns>True if executed correctly, or objectives have been met</returns>
    public virtual bool RunAct(Quest quest, int currentObjectiveCount)
    {
        return false;
    }

    public virtual int MaxObjective()
    {
        return ParentQuestTemplate.LetItDone ? Count * 3 / 2 : Count;
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    public void SetObjective(Quest quest, int value)
    {
        if (quest != null)
            quest.Objectives[ThisComponentObjectiveIndex] = value;
    }

    /// <summary>
    /// Get Current Objective Count for this Act (forwarded value from Quest)
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    public int GetObjective(Quest quest)
    {
        return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    public int AddObjective(Quest quest, int amount)
    {
        if (quest == null)
            return 0;
        quest.Objectives[ThisComponentObjectiveIndex] += amount;
        return quest.Objectives[ThisComponentObjectiveIndex];
    }

    /// <summary>
    /// Called when a quest ended or otherwise removed, use to clean up items and tasks
    /// </summary>
    /// <param name="quest"></param>
    public virtual void QuestCleanup(Quest quest)
    {
        // Nothing by default
    }

    /// <summary>
    /// Called when a quest got dropped by the player
    /// </summary>
    /// <param name="quest"></param>
    public virtual void QuestDropped(Quest quest)
    {
        // Nothing by default
    }
}
