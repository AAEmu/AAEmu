using System.Linq;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public class QuestActTemplate
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    /// <summary>
    /// quest_act_xxx Id
    /// </summary>
    public uint Id { get; set; }

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
    public virtual void Start()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct started {Id}.");
    }

    /// <summary>
    /// Called for every QuestAct in a component when the component is fully completed
    /// </summary>
    public virtual void Complete()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct completed {Id}.");
    }

    /// <summary>
    /// Checks is this specific QuestAct is completed (checks objectives)
    /// </summary>
    /// <returns></returns>
    public virtual bool IsCompleted()
    {
        return false;
    }

    /// <summary>
    /// Returns the objective count for this QuestAct
    /// </summary>
    /// <returns></returns>
    public virtual int GetCount()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct {Id} GetCount()");
        return 0;
    }

    /// <summary>
    /// Moves objective for this QuestAct one further
    /// </summary>
    public virtual void Update()
    {
        Logger.Info($"{QuestActTemplateName} - QuestAct {Id} has been updated.");
    }

    /// <summary>
    /// Resets objective for this QuestAct
    /// </summary>
    public virtual void ClearStatus()
    {
        Logger.Info($"{QuestActTemplateName} - Reset QuestAct {Id} objectives.");
    }

    public virtual bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Info($"{QuestActTemplateName} - Use QuestAct {Id}, Character: {character.Name}, Objective {objective}.");
        return false;
    }
}
