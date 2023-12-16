using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events
{
    internal class IndunEventNpcCombatEndeds : IndunEvent
    {
        public uint NpcId { get; set; }

        public override void Subscribe(InstanceWorld world)
        {
            world.Events.OnUnitCombatEnd += OnUnitCombatEnd;
        }

        public override void UnSubscribe(InstanceWorld world)
        {
            world.Events.OnUnitCombatEnd -= OnUnitCombatEnd;
        }

        private void OnUnitCombatEnd(object sender, OnUnitCombatEndArgs args)
        {
            if (args.Npc is Npc npc)
            {
                Logger.Warn($"{npc.TemplateId} has left combat.");
            }
        }
    }
}
