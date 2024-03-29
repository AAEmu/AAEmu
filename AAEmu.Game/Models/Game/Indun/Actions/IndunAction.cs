using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

public class IndunAction
{
    protected static Logger Logger = LogManager.GetCurrentClassLogger();
        
    public uint Id { get; set; }
    public uint DetailId { get; set; }
    public uint ZoneGroupId { get; set; }
    public uint NextActionId { get; set; }

    public virtual void Execute(InstanceWorld world) { }
}