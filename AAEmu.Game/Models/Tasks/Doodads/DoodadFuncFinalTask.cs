using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncFinalTask : DoodadFuncTask
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Unit _caster;
        private Doodad _owner;
        private uint _skillId;
        private int _nextPhase;
        private bool _respawn;
        private int _delay;
        private DateTime? _respawnTime;

        public DoodadFuncFinalTask(Unit caster, Doodad owner, uint skillId, bool respawn, int delay) : base(caster, owner, skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
            _nextPhase = (int)owner.FuncGroupId;
            _respawn = respawn;
            _owner = owner;
            _delay = delay;
        }

        public override void Execute()
        {
            _log.Debug("[Doodad] DoodadFuncFinalTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
            
            if (_respawn && _owner.Spawner != null)
            {
                if (_respawnTime == null && _owner.FuncTask != null)
                {
                    _respawnTime = DateTime.UtcNow;
                    TaskManager.Instance.Schedule(_owner.FuncTask, TimeSpan.FromMilliseconds(_delay));
                    return;
                }

                //if (_owner.FuncTask != null)
                //{
                //    _ = _owner.FuncTask.Cancel();
                //    _owner.FuncTask = null;
                //    _log.Debug("DoodadFuncFinalTask: The current timer has been ended.");
                //}
                _owner.Spawner.Despawn(_owner);

                var world = WorldManager.Instance.GetWorld(_owner.Transform.WorldId);
                //_owner.Spawner.DecreaseCount(_owner);
                _owner.Spawner.Position.WorldId = world.Id;
                _owner.Spawner.Spawn(0);
            }
            else
            {
                //if (_owner.FuncTask != null)
                //{
                //    _ = _owner.FuncTask.Cancel();
                //    _owner.FuncTask = null;
                //    _log.Debug("DoodadFuncFinalTask: The current timer has been ended.");
                //}

                if (_owner.Spawner != null)
                {
                    _owner.Spawner?.Despawn(_owner);
                }
                else
                {
                    _owner.Delete();
                }
            }
        }
    }
}
