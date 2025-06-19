using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

internal class IndunActionSetRoomCleareds : IndunAction
{
    public uint IndunRoomId { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        worldInstance.DungeonInstance?.SetRoomCleared(IndunRoomId);
        Logger.Warn($"Room Clear: {IndunRoomId}");

        worldInstance.Events.OnAreaClear(worldInstance, new OnAreaClearArgs());
    }
}
