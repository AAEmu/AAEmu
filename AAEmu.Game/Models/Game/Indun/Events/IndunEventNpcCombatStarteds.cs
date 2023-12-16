using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events
{
    internal class IndunEventNpcCombatStarteds : IndunEvent
    {
        public uint NpcId { get; set; }

        public override void Subscribe(InstanceWorld world)
        {
            world.Events.OnUnitCombatStart += OnNpcCombatStarted;
        }

        public override void UnSubscribe(InstanceWorld world)
        {
            world.Events.OnUnitCombatStart -= OnNpcCombatStarted;
        }

        private void OnNpcCombatStarted(object sender, OnUnitCombatStartArgs args)
        {
            if (args.Npc is not Npc npc || sender is not InstanceWorld world) { return; }
            if (npc.TemplateId != NpcId) { return; }

            Logger.Warn($"{npc.TemplateId} has entered combat.");

            //var action = IndunGameData.Instance.GetIndunActionById(StartActionId);
            //action.Execute(world);
            
            //IndunManager.DoIndunActions(StartActionId, world);
        }
    }
}
