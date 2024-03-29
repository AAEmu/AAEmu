using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNpcSpawneds : IndunEvent
{
    public uint NpcId { get; set; }

    public override void Subscribe(InstanceWorld world)
    {
            world.Events.OnUnitSpawn += OnUnitSpawn;
        }

    public override void UnSubscribe(InstanceWorld world)
    {
            world.Events.OnUnitSpawn -= OnUnitSpawn;
        }

    private void OnUnitSpawn(object sender, OnUnitSpawnArgs args)
    {
            if (args.Npc is not Npc npc || sender is not InstanceWorld world) { return; }

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