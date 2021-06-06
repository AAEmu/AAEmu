using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncFinalTask : DoodadFuncTask
    {
        private bool _respawn;
        private int _delay;
        private DateTime? _respawnTime;

        public DoodadFuncFinalTask(Unit caster, Doodad owner, uint skillId, bool respawn, int delay) : base(caster, owner, skillId)
        {
            _respawn = respawn;
            _owner = owner;
            _delay = delay;
        }

        public override void Execute()
        {
            if (_respawn && _owner.Spawner != null)
            {
                if (_respawnTime is null)
                {
                    _respawnTime = DateTime.Now;
                    TaskManager.Instance.Schedule(_owner.FuncTask, TimeSpan.FromMilliseconds(_delay));
                    return;
                }
                var world = WorldManager.Instance.GetWorld(_owner.Position.WorldId);
                _owner.Spawner.DecreaseCount(_owner);
                _owner.Spawner.Position.WorldId = world.Id;
                _owner.Spawner.Spawn(0);
            }
            else
            {
                _owner.FuncTask = null;
                _owner.Delete();
            }
        }
    }
}
