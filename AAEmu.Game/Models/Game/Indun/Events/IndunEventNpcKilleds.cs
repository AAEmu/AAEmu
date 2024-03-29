using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcKilleds : IndunEvent
{
    public uint NpcId { get; set; }

    public override void Subscribe(InstanceWorld world)
    {
            world.Events.OnUnitKilled += OnUnitKilled;
        }

    public override void UnSubscribe(InstanceWorld world)
    {
            world.Events.OnUnitKilled -= OnUnitKilled;
        }

    private void OnUnitKilled(object sender, OnUnitKilledArgs args)
    {
            if (args.Victim is not Npc npc || sender is not InstanceWorld world) { return; }
            if (npc.TemplateId != NpcId) { return; }

            Logger.Warn($"IndunEventNpcKilleds - {NpcId}, {Id}");
            IndunManager.DoIndunActions(StartActionId, world);
        }
}