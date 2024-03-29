using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventDoodadSpawneds : IndunEvent
{
    public uint DoodadAlmightyId { get; set; } // templateId
    public uint DoodadFuncGroupId { get; set; }

    public override void Subscribe(InstanceWorld world)
    {
            world.Events.OnDoodadSpawn += OnDoodadSpawn;
        }

    public override void UnSubscribe(InstanceWorld world)
    {
            world.Events.OnDoodadSpawn += OnDoodadSpawn;
        }

    private void OnDoodadSpawn(object sender, OnDoodadSpawnArgs args)
    {
            var doodad = args.Doodad;
            if (doodad == null || sender is not InstanceWorld world) { return; }
            Logger.Warn($"IndunEventDoodadSpawneds - {doodad.TemplateId}, {DoodadAlmightyId}");
            if (doodad.TemplateId != DoodadAlmightyId) { return; }

            var action = IndunGameData.Instance.GetIndunActionById(StartActionId);
            action.Execute(world);

            //IndunManager.DoIndunActions(StartActionId, world);
        }
}