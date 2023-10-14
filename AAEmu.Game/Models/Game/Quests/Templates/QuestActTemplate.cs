using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public abstract class QuestActTemplate
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; set; }

    public void Start()
    {
        Logger.Info("Акт начат.");
    }
    public void Complete()
    {
        Logger.Info("Акт завершен.");
    }
    public virtual bool IsCompleted()
    {
        return false;
    }
    public virtual int GetCount()
    {
        Logger.Info("Получим, информацию на сколько выполнено задание.");
        return 0;
    }
    public virtual void Update()
    {
        Logger.Info("Акт обновлен.");
    }
    public virtual void ClearStatus()
    {
        Logger.Info("Сбросили статус в ноль.");
    }

    public abstract bool Use(ICharacter character, Quest quest, int objective);

}
