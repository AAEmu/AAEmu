using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.Indun.Actions;

public class IndunAction
{
    protected readonly static Logger Logger = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public uint DetailId { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint NextActionId { get; set; }

    public virtual void Execute(WorldInstance worldInstance) { }
}
