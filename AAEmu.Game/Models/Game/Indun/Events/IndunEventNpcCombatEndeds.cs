using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcCombatEndeds : IndunEvent
{
    public uint NpcId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitCombatEnd += OnUnitCombatEnd;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitCombatEnd -= OnUnitCombatEnd;
    }

    private void OnUnitCombatEnd(object sender, OnUnitCombatEndArgs args)
    {
        if (args.Npc is Npc npc)
        {
            Logger.Warn($"{npc.TemplateId} has left combat.");
        }
    }
}
