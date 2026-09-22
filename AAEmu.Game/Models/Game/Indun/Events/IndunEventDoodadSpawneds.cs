using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventDoodadSpawneds : IndunEvent
{
    public uint DoodadAlmightyId { get; set; } // templateId
    public uint DoodadFuncGroupId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnDoodadSpawn += OnDoodadSpawn;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        // Was "+=", which stacked one more handler on every unsubscribe.
        worldInstance.Events.OnDoodadSpawn -= OnDoodadSpawn;
    }

    private void OnDoodadSpawn(object sender, OnDoodadSpawnArgs args)
    {
        var doodad = args.Doodad;
        if (doodad == null || sender is not WorldInstance world) { return; }
        if (doodad.TemplateId != DoodadAlmightyId) { return; }

        Logger.Debug($"IndunEventDoodadSpawneds - {doodad.TemplateId}, {DoodadAlmightyId}");
        // The whole next_action_id chain, not only its first action.
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
