using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcCombatStarteds : IndunEvent
{
    public uint NpcId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitCombatStart += OnNpcCombatStarted;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitCombatStart -= OnNpcCombatStarted;
    }

    private void OnNpcCombatStarted(object sender, OnUnitCombatStartArgs args)
    {
        if (args.Npc is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId) { return; }

        Logger.Debug($"IndunEventNpcCombatStarteds - {NpcId} ({npc.ObjId}) has entered combat.");
        // Raised by Unit.SetBattleState on the NPC's own flag transition, once per engagement.
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
