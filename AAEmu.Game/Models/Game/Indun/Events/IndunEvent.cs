using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.Indun.Events;

public class IndunEvent
{
    protected readonly static Logger Logger = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public uint ConditionId { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint StartActionId { get; set; }

    public virtual void Subscribe(WorldInstance worldInstance)
    {
    }

    public virtual void UnSubscribe(WorldInstance worldInstance)
    {
    }
}
