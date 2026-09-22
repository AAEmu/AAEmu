using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcSpawneds : IndunEvent
{
    public uint NpcId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitSpawn += OnUnitSpawn;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnUnitSpawn -= OnUnitSpawn;
    }

    private void OnUnitSpawn(object sender, OnUnitSpawnArgs args)
    {
        if (args.Npc is not Npc npc || sender is not WorldInstance world) { return; }
        if (npc.TemplateId != NpcId) { return; }

        Logger.Debug($"IndunEventNpcSpawneds - {NpcId}");
        // The whole next_action_id chain, not only its first action.
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
