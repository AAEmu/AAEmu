using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events
{
    public class IndunEvent
    {
        protected static Logger Logger = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public uint ConditionId { get; set; }
        public uint ZoneGroupId { get; set; }
        public uint StartActionId { get; set; }

        public virtual void Subscribe(InstanceWorld world)
        {
        }

        public virtual void UnSubscribe(InstanceWorld world)
        {
        }
    }
}
