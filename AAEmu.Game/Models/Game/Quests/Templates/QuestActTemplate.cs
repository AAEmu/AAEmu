using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public abstract class QuestActTemplate
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; set; }

    public void Start()
    {
        Logger.Debug("Акт начат.");
    }
    public void Complete()
    {
        Logger.Debug("Акт завершен.");
    }
    public virtual bool IsCompleted()
    {
        return false;
    }
    public virtual int GetCount()
    {
        Logger.Debug("Получим, сколько уже имеем предметов по заданию.");
        return 0;
    }
    public virtual void Update()
    {
        Logger.Debug("Акт обновлен.");
    }

    public abstract bool Use(ICharacter character, Quest quest, int objective);

}
