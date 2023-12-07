using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Actions
{
    internal class IndunActionSetRoomCleareds : IndunAction
    {
        public uint IndunRoomId { get; set; }

        public override void Execute(InstanceWorld world)
        {
            IndunManager.Instance.SetRoomCleared(IndunRoomId, world);
            Logger.Warn($"Room Clear: {IndunRoomId}");

            world.Events.OnAreaClear(world, new OnAreaClearArgs());
        }
    }
}
