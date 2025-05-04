using AAEmu.Game.GameData;
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

        if (npc.TemplateId != NpcId)
        {
            Logger.Warn($"IndunEventNpcSpawneds - need npc={npc.TemplateId}, not npc={NpcId}");
            return;
        }

        Logger.Warn($"IndunEventNpcSpawneds - {NpcId}");
        var action = IndunGameData.Instance.GetIndunActionById(StartActionId);
        action.Execute(world);
    }
}
